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
    private const int GlobalLimit = 16;
    private const int PerPluginLimit = 3;
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
        resources.Own("notifications", PluginResourceKind.UserInterface, new OwnerCleanup(this, manifest.Id));
        return new Service(this, manifest.Id);
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
        }
    }

    private void PublishInternal(PluginId owner, string message, TimeSpan duration, PluginNotificationOptions options)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RemoveExpired(now);
        for (int index = 0; index < notifications.Count; index++)
        {
            PluginNotification existing = notifications[index];
            if (existing.Owner == owner && string.Equals(existing.Message, message, StringComparison.Ordinal) && existing.Options.Target == options.Target && existing.Options.Color.Equals(options.Color))
            {
                notifications[index] = new PluginNotification(owner, message, now.Add(duration), options);
                snapshotDirty = true;
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
    }

    private sealed class Service : IPluginNotificationService
    {
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(15);
        private readonly PluginNotificationCenter center;
        private readonly PluginId owner;

        public Service(PluginNotificationCenter center, PluginId owner) { this.center = center; this.owner = owner; }

        public void Show(string message, PluginNotificationOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A notification message is required.", nameof(message));
            TimeSpan effective = options?.Duration ?? DefaultDuration;
            if (effective <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
            if (effective > MaximumDuration) effective = MaximumDuration;
            lock (center.gate) center.PublishInternal(owner, message, effective, options ?? new PluginNotificationOptions());
        }
    }

    private sealed class OwnerCleanup : IDisposable
    {
        private PluginNotificationCenter? center;
        private readonly PluginId owner;

        public OwnerCleanup(PluginNotificationCenter center, PluginId owner)
        {
            this.center = center;
            this.owner = owner;
        }

        public void Dispose()
        {
            PluginNotificationCenter? current = center;
            center = null;
            if (current != null) current.RemoveOwner(owner);
        }
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
