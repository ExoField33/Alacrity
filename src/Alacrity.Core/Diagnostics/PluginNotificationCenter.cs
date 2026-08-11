using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Transient application notifications for plugin state changes; entries are never persisted.</summary>
public sealed class PluginNotificationCenter
{
    private readonly List<PluginNotification> notifications = new List<PluginNotification>();
    private readonly object gate = new object();
    private PluginNotification[] activeSnapshot = Array.Empty<PluginNotification>();
    private bool snapshotDirty = true;
    private int activeCount;
    private readonly Dictionary<PluginId, PublicationWindow> publicationWindows = new Dictionary<PluginId, PublicationWindow>();
    private const int GlobalLimit = 16;
    private const int PerPluginLimit = 3;
    private const int PerPluginPublicationsPerWindow = 8;
    private static readonly TimeSpan PublicationWindowDuration = TimeSpan.FromSeconds(10);
    /// <summary>Publishes a transient notification with a bounded on-screen lifetime.</summary>
    public void Publish(string message, TimeSpan duration, PluginNotificationOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A notification message is required.", nameof(message));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (gate)
            PublishInternal(default, message, duration, options ?? new PluginNotificationOptions());
    }
    /// <summary>Returns notifications that have not expired and removes expired entries.</summary>
    public IReadOnlyList<PluginNotification> GetActive(DateTimeOffset now)
    {
        if (System.Threading.Volatile.Read(ref activeCount) == 0)
        {
            return Array.Empty<PluginNotification>();
        }

        lock (gate)
        {
            RemoveExpired(now);
            if (snapshotDirty)
            {
                activeSnapshot = notifications.Count == 0 ? Array.Empty<PluginNotification>() : notifications.ToArray();
                snapshotDirty = false;
            }
            return activeSnapshot;
        }
    }

    /// <summary>Creates a manifest-scoped publisher without exposing notification storage to plugins.</summary>
    public IPluginNotificationService CreateService(PluginManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        manifest.Validate();
        return new Service(this, manifest.Id);
    }

    /// <summary>Creates a scope-owned publisher and removes its pending messages when that scope ends.</summary>
    public IPluginNotificationService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        manifest.Validate();
        var cleanup = new OwnerCleanup(this, manifest.Id);
        try
        {
            resources.Own("notifications", PluginResourceKind.UserInterface, cleanup);
        }
        catch
        {
            cleanup.Dispose();
            throw;
        }
        return new Service(this, manifest.Id, cleanup);
    }

    /// <summary>Removes pending notifications owned by a plugin as part of lifecycle cleanup.</summary>
    public void RemoveOwner(PluginId owner)
    {
        if (!owner.IsValid) return;
        lock (gate)
        {
            bool removed = false;
            for (int index = notifications.Count - 1; index >= 0; index--)
            {
                if (notifications[index].Owner != owner) continue;
                notifications.RemoveAt(index);
                removed = true;
            }
            if (removed) snapshotDirty = true;
            publicationWindows.Remove(owner);
            UpdateActiveCount();
        }
    }

    private void PublishInternal(PluginId owner, string message, TimeSpan duration, PluginNotificationOptions options)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RemoveExpired(now);
        if (owner.IsValid && !TryConsumePublication(owner, now))
            return;
        for (int index = 0; index < notifications.Count; index++)
        {
            PluginNotification existing = notifications[index];
            if (existing.Owner == owner && string.Equals(existing.Message, message, StringComparison.Ordinal) && existing.Options.Target == options.Target && existing.Options.Color.Equals(options.Color))
            {
                notifications[index] = new PluginNotification(owner, message, now.Add(duration), options);
                snapshotDirty = true;
                UpdateActiveCount();
                return;
            }
        }
        if (owner.IsValid)
        {
            int owned = 0;
            for (int index = notifications.Count - 1; index >= 0; index--)
                if (notifications[index].Owner == owner && ++owned >= PerPluginLimit) notifications.RemoveAt(index);
        }
        while (notifications.Count >= GlobalLimit) notifications.RemoveAt(0);
        notifications.Add(new PluginNotification(owner, message, now.Add(duration), options));
        snapshotDirty = true;
        UpdateActiveCount();
    }

    private bool TryConsumePublication(PluginId owner, DateTimeOffset now)
    {
        if (!publicationWindows.TryGetValue(owner, out PublicationWindow window) || now - window.StartedAt > PublicationWindowDuration)
        {
            publicationWindows[owner] = new PublicationWindow(now, 1);
            return true;
        }
        if (window.Count >= PerPluginPublicationsPerWindow)
            return false;
        publicationWindows[owner] = new PublicationWindow(window.StartedAt, window.Count + 1);
        return true;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        bool removed = false;
        for (int index = notifications.Count - 1; index >= 0; index--)
        {
            if (notifications[index].ExpiresAt > now) continue;
            notifications.RemoveAt(index);
            removed = true;
        }
        if (removed) snapshotDirty = true;
        if (removed)
        {
            UpdateActiveCount();
        }
    }

    private void UpdateActiveCount()
    {
        System.Threading.Volatile.Write(ref activeCount, notifications.Count);
    }

    private sealed class Service : IPluginNotificationService
    {
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(15);
        private readonly PluginNotificationCenter center;
        private readonly PluginId owner;
        private readonly OwnerCleanup? guard;

        public Service(PluginNotificationCenter center, PluginId owner, OwnerCleanup? guard = null) { this.center = center; this.owner = owner; this.guard = guard; }

        public void Show(string message, PluginNotificationOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A notification message is required.", nameof(message));
            TimeSpan effective = options?.Duration ?? DefaultDuration;
            if (effective <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
            if (effective > MaximumDuration) effective = MaximumDuration;
            lock (center.gate)
            {
                if (guard != null && guard.IsReleased) throw new ObjectDisposedException("IPluginNotificationService", "The owning plugin scope has been released.");
                center.PublishInternal(owner, message, effective, options ?? new PluginNotificationOptions());
            }
        }
    }

    private sealed class OwnerCleanup : IDisposable
    {
        private readonly PluginNotificationCenter center;
        private readonly PluginId owner;
        private int released;

        public OwnerCleanup(PluginNotificationCenter center, PluginId owner)
        {
            this.center = center;
            this.owner = owner;
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref released, 1) == 0)
                center.RemoveOwner(owner);
        }

        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
    }

    private readonly struct PublicationWindow
    {
        internal PublicationWindow(DateTimeOffset startedAt, int count) { StartedAt = startedAt; Count = count; }
        internal DateTimeOffset StartedAt { get; }
        internal int Count { get; }
    }
}

/// <summary>One non-persistent plugin notification.</summary>
public sealed class PluginNotification
{
    internal PluginNotification(PluginId owner, string message, DateTimeOffset expiresAt, PluginNotificationOptions options) { Owner = owner; Message = message; ExpiresAt = expiresAt; Options = options; }
    /// <summary>Structured owning plugin identity, when the message originated from a plugin.</summary>
    public PluginId Owner { get; }
    public string Message { get; }
    public DateTimeOffset ExpiresAt { get; }
    public PluginNotificationOptions Options { get; }
}
