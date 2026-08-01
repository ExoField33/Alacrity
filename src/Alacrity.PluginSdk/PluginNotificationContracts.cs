using System;

namespace Alacrity.PluginSdk;

/// <summary>Host presentation surfaces that can receive a transient plugin notification.</summary>
[Flags]
public enum PluginNotificationTarget
{
    /// <summary>Shows in the main plugin manager status area.</summary>
    PluginManager = 1,
    /// <summary>Shows through the normal in-game notification draw path.</summary>
    InGame = 2,
    /// <summary>Shows on every currently supported host surface.</summary>
    All = PluginManager | InGame
}

/// <summary>Immutable host-neutral notification presentation options.</summary>
public sealed class PluginNotificationOptions
{
    /// <summary>Creates a notification declaration with safe defaults.</summary>
    public PluginNotificationOptions(PluginNotificationTarget target = PluginNotificationTarget.All, PluginColor? color = null, TimeSpan? duration = null)
    {
        if (target == 0 || (target & ~PluginNotificationTarget.All) != 0) throw new ArgumentOutOfRangeException(nameof(target));
        if (duration.HasValue && duration.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Target = target;
        Color = color;
        Duration = duration;
    }

    /// <summary>Selected host presentation surfaces.</summary>
    public PluginNotificationTarget Target { get; }
    /// <summary>Optional presentation color; the host uses its normal default when absent.</summary>
    public PluginColor? Color { get; }
    /// <summary>Optional bounded lifetime requested by the plugin.</summary>
    public TimeSpan? Duration { get; }
}

/// <summary>Publishes bounded, host-rendered transient notifications for the owning plugin.</summary>
public interface IPluginNotificationService
{
    /// <summary>Shows a transient notification. The host clamps the lifetime to protect the shared UI surface.</summary>
    void Show(string message, PluginNotificationOptions? options = null);
}
