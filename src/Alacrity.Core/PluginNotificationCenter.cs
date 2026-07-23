using System;
using System.Collections.Generic;
using System.Linq;

namespace Alacrity.Core;

/// <summary>Transient application notifications for plugin state changes; entries are never persisted.</summary>
public sealed class PluginNotificationCenter
{
    private readonly List<PluginNotification> notifications = new List<PluginNotification>();
    /// <summary>Publishes a transient notification with a bounded on-screen lifetime.</summary>
    public void Publish(string message, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A notification message is required.", nameof(message));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        notifications.Add(new PluginNotification(message, DateTimeOffset.UtcNow.Add(duration)));
    }
    /// <summary>Returns notifications that have not expired and removes expired entries.</summary>
    public IReadOnlyList<PluginNotification> GetActive(DateTimeOffset now)
    {
        notifications.RemoveAll(notification => notification.ExpiresAt <= now);
        return notifications.ToArray();
    }
}

/// <summary>One non-persistent plugin notification.</summary>
public sealed class PluginNotification
{
    internal PluginNotification(string message, DateTimeOffset expiresAt) { Message = message; ExpiresAt = expiresAt; }
    public string Message { get; }
    public DateTimeOffset ExpiresAt { get; }
}
