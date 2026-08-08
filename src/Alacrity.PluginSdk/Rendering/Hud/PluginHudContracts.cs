using System;

namespace Alacrity.PluginSdk;

/// <summary>Immutable metadata for a retained gameplay HUD widget.</summary>
public sealed class PluginHudWidgetDescriptor
{
    /// <summary>Creates a widget declaration with an owner-local identifier and deterministic order.</summary>
    public PluginHudWidgetDescriptor(string id, int order = 0)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A HUD widget ID is required.", nameof(id)) : id;
        Order = order;
    }

    /// <summary>Gets the owner-local widget identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the host draw order among gameplay HUD widgets.</summary>
    public int Order { get; }
}

/// <summary>Immutable frame data for a gameplay HUD draw callback.</summary>
public readonly struct PluginHudFrame
{
    /// <summary>Creates a host-captured HUD frame.</summary>
    public PluginHudFrame(int screenWidth, int screenHeight, float uiScale, TimeSpan presentationTime, long simulationVersion)
    {
        if (screenWidth < 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight < 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        if (float.IsNaN(uiScale) || float.IsInfinity(uiScale) || uiScale <= 0f) throw new ArgumentOutOfRangeException(nameof(uiScale));
        ScreenWidth = screenWidth; ScreenHeight = screenHeight; UiScale = uiScale; PresentationTime = presentationTime; SimulationVersion = simulationVersion;
    }

    /// <summary>Gets the current HUD width in pixels.</summary>
    public int ScreenWidth { get; }
    /// <summary>Gets the current HUD height in pixels.</summary>
    public int ScreenHeight { get; }
    /// <summary>Gets Terraria's current UI scale.</summary>
    public float UiScale { get; }
    /// <summary>Gets a monotonic host presentation timestamp.</summary>
    public TimeSpan PresentationTime { get; }
    /// <summary>Gets the current simulation/update identity.</summary>
    public long SimulationVersion { get; }
}

/// <summary>Safe host-rendered gameplay HUD surface. It never exposes Terraria or XNA rendering objects.</summary>
public interface IPluginHudCanvas
{
    /// <summary>Draws a Terraria-style panel.</summary>
    void DrawPanel(PluginUiRect bounds, PluginOverlayColor color);
    /// <summary>Draws text in HUD coordinates.</summary>
    void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f, float originX = 0f, float originY = 0f);
    /// <summary>Draws a host-approved texture asset.</summary>
    void DrawAsset(string approvedAssetId, PluginUiRect bounds, PluginOverlayColor? tint = null);
    /// <summary>Draws a player avatar from the host's current presentation state.</summary>
    void DrawPlayerAvatar(int playerId, float x, float y, float scale = 1f);
    /// <summary>Draws an NPC map-head icon from a host-approved NPC type.</summary>
    void DrawNpcHead(int npcType, float x, float y, float scale = 1f, PluginOverlayColor? tint = null);
    /// <summary>Draws and activates an owner-local icon interaction using a host-approved asset.</summary>
    void DrawInteractiveAsset(string interactionId, string approvedAssetId, PluginUiRect bounds);
    /// <summary>Draws and activates an owner-local icon interaction using an NPC map-head icon.</summary>
    void DrawInteractiveNpcHead(string interactionId, int npcType, PluginUiRect bounds);
    /// <summary>Marks the pointer as consumed while it is inside a HUD region.</summary>
    bool CapturePointer(PluginUiRect bounds);
}

/// <summary>Registers retained HUD widgets that are released automatically with the owning plugin scope.</summary>
public interface IPluginHudService
{
    /// <summary>Registers a deterministic gameplay HUD widget.</summary>
    IPluginRegistration Register(PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw);
}

/// <summary>Host-facing renderer used by Core to dispatch a widget with its verified owner.</summary>
public interface IPluginHudRenderer
{
    /// <summary>Renders one widget through a canvas scoped to <paramref name="owner"/>.</summary>
    void Render(PluginId owner, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw, PluginHudFrame frame);
}

/// <summary>Read-only session data suitable for HUD presentation.</summary>
public readonly struct PluginSessionPresentationSnapshot
{
    /// <summary>Creates a detached session snapshot.</summary>
    public PluginSessionPresentationSnapshot(string serverName, int playerCapacity, int? pingMilliseconds)
    {
        ServerName = serverName ?? string.Empty;
        PlayerCapacity = playerCapacity < 0 ? 0 : playerCapacity;
        PingMilliseconds = pingMilliseconds < 0 ? null : pingMilliseconds;
    }
    /// <summary>Gets the current world or server display name.</summary>
    public string ServerName { get; }
    /// <summary>Gets the reported server player capacity.</summary>
    public int PlayerCapacity { get; }
    /// <summary>Gets sampled latency, or null when the source is unavailable.</summary>
    public int? PingMilliseconds { get; }
}

/// <summary>Provides host-captured session data without exposing Terraria networking internals.</summary>
public interface IPluginSessionPresentationService
{
    /// <summary>Gets the latest bounded session presentation snapshot.</summary>
    PluginSessionPresentationSnapshot GetCurrent();
}
