using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Transient application notifications for plugin state changes; entries are never persisted.</summary>
public sealed class PluginNotificationCenter
{
    private readonly List<PluginNotification> notifications = new List<PluginNotification>();
    private readonly object gate = new object();
    /// <summary>Publishes a transient notification with a bounded on-screen lifetime.</summary>
    public void Publish(string message, TimeSpan duration, PluginNotificationOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A notification message is required.", nameof(message));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (gate)
            notifications.Add(new PluginNotification(message, DateTimeOffset.UtcNow.Add(duration), options ?? new PluginNotificationOptions()));
    }
    /// <summary>Returns notifications that have not expired and removes expired entries.</summary>
    public IReadOnlyList<PluginNotification> GetActive(DateTimeOffset now)
    {
        lock (gate)
        {
            notifications.RemoveAll(notification => notification.ExpiresAt <= now);
            return notifications.ToArray();
        }
    }

    /// <summary>Creates a manifest-scoped publisher without exposing notification storage to plugins.</summary>
    public IPluginNotificationService CreateService(PluginManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        manifest.Validate();
        return new Service(this, manifest.Id);
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
            center.Publish(owner.Value + ": " + message, effective, options);
        }
    }
}

/// <summary>One non-persistent plugin notification.</summary>
public sealed class PluginNotification
{
    internal PluginNotification(string message, DateTimeOffset expiresAt, PluginNotificationOptions options) { Message = message; ExpiresAt = expiresAt; Options = options; }
    public string Message { get; }
    public DateTimeOffset ExpiresAt { get; }
    public PluginNotificationOptions Options { get; }
}
