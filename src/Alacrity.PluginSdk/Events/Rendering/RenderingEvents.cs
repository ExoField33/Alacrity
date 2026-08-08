using System;

namespace Alacrity.PluginSdk;

/// <summary>
/// Published from the verified Terraria world-overlay draw entry point. The event is render-phase
/// metadata only and never exposes a SpriteBatch or mutable game state.
/// </summary>
public readonly struct WorldOverlayRenderingEvent : IRenderThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates a world-overlay rendering event from a monotonic presentation timestamp.</summary>
    public WorldOverlayRenderingEvent(TimeSpan timestamp) { Timestamp = timestamp; }
    /// <summary>Monotonic host presentation time.</summary>
    public TimeSpan Timestamp { get; }
}

/// <summary>Published from the verified Terraria HUD-overlay draw entry point.</summary>
public readonly struct HudRenderingEvent : IRenderThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates a HUD rendering event from a monotonic presentation timestamp.</summary>
    public HudRenderingEvent(TimeSpan timestamp) { Timestamp = timestamp; }
    /// <summary>Monotonic host presentation time.</summary>
    public TimeSpan Timestamp { get; }
}

/// <summary>Published from the verified Terraria menu-overlay draw entry point.</summary>
public readonly struct MenuRenderingEvent : IRenderThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates a menu rendering event from a monotonic presentation timestamp.</summary>
    public MenuRenderingEvent(TimeSpan timestamp) { Timestamp = timestamp; }
    /// <summary>Monotonic host presentation time.</summary>
    public TimeSpan Timestamp { get; }
}
