using System;

namespace Alacrity.PluginSdk;

/// <summary>Host-rendered overlay layers in deterministic draw order.</summary>
public enum PluginOverlayLayer
{
    /// <summary>Draws before world-marker and foreground contributions.</summary>
    Background = 0,
    /// <summary>Draws approved world-to-screen markers.</summary>
    WorldMarkers = 100,
    /// <summary>Draws after lower overlay layers.</summary>
    Foreground = 200
}

/// <summary>Coordinate space and verified Terraria draw phase for a host-rendered overlay.</summary>
public enum PluginOverlaySpace
{
    /// <summary>World coordinates projected through the active Terraria camera.</summary>
    World = 0,
    /// <summary>Gameplay HUD coordinates after Terraria's UI transform is active.</summary>
    Hud = 1,
    /// <summary>Main-menu coordinates while Terraria is drawing its menu UI.</summary>
    Menu = 2
}

/// <summary>Immutable declaration for one host-owned overlay callback.</summary>
public sealed class PluginOverlayDescriptor
{
    /// <summary>Creates an immutable overlay declaration.</summary>
    public PluginOverlayDescriptor(string id, PluginOverlayLayer layer = PluginOverlayLayer.Foreground, int order = 0, PluginOverlaySpace space = PluginOverlaySpace.World)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("An overlay ID is required.", nameof(id)) : id;
        if (!Enum.IsDefined(typeof(PluginOverlayLayer), layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        if (!Enum.IsDefined(typeof(PluginOverlaySpace), space)) throw new ArgumentOutOfRangeException(nameof(space));
        Layer = layer;
        Order = order;
        Space = space;
    }

    /// <summary>Stable overlay ID within its plugin.</summary>
    public string Id { get; }
    /// <summary>Host draw layer.</summary>
    public PluginOverlayLayer Layer { get; }
    /// <summary>Order within the declared layer.</summary>
    public int Order { get; }
    /// <summary>Host coordinate space and draw phase for this contribution.</summary>
    public PluginOverlaySpace Space { get; }
}

/// <summary>Immutable draw-frame data supplied by the host at the documented overlay phase.</summary>
public readonly struct PluginOverlayFrame
{
    /// <summary>Creates an immutable host draw-frame snapshot.</summary>
    public PluginOverlayFrame(int screenWidth, int screenHeight, float uiScale, bool isGameMenu, TimeSpan presentationTime)
        : this(screenWidth, screenHeight, uiScale, isGameMenu, presentationTime, 0)
    {
    }

    /// <summary>Creates an immutable host draw-frame snapshot with a separate simulation identity.</summary>
    public PluginOverlayFrame(int screenWidth, int screenHeight, float uiScale, bool isGameMenu, TimeSpan presentationTime, long simulationVersion)
    {
        if (screenWidth < 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight < 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        if (float.IsNaN(uiScale) || float.IsInfinity(uiScale) || uiScale <= 0f) throw new ArgumentOutOfRangeException(nameof(uiScale));
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        UiScale = uiScale;
        IsGameMenu = isGameMenu;
        PresentationTime = presentationTime;
        SimulationVersion = simulationVersion;
    }

    /// <summary>Current drawable width in pixels.</summary>
    public int ScreenWidth { get; }
    /// <summary>Current drawable height in pixels.</summary>
    public int ScreenHeight { get; }
    /// <summary>Current UI scale.</summary>
    public float UiScale { get; }
    /// <summary>Whether Terraria is currently in a menu.</summary>
    public bool IsGameMenu { get; }
    /// <summary>Host-local presentation timestamp.</summary>
    public TimeSpan PresentationTime { get; }
    /// <summary>Host simulation tick or version; it is not a presentation timestamp.</summary>
    public long SimulationVersion { get; }
}

/// <summary>Value color passed to host-rendered overlay commands.</summary>
public readonly struct PluginOverlayColor : IEquatable<PluginOverlayColor>
{
    /// <summary>Creates an RGBA draw color.</summary>
    public PluginOverlayColor(byte red, byte green, byte blue, byte alpha = byte.MaxValue) { Red = red; Green = green; Blue = blue; Alpha = alpha; }
    /// <summary>Red channel.</summary>
    public byte Red { get; }
    /// <summary>Green channel.</summary>
    public byte Green { get; }
    /// <summary>Blue channel.</summary>
    public byte Blue { get; }
    /// <summary>Alpha channel.</summary>
    public byte Alpha { get; }
    /// <inheritdoc />
    public bool Equals(PluginOverlayColor other) => Red == other.Red && Green == other.Green && Blue == other.Blue && Alpha == other.Alpha;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PluginOverlayColor other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => (Red << 24) | (Green << 16) | (Blue << 8) | Alpha;
}

/// <summary>Safe command surface interpreted by the host; it never exposes SpriteBatch or game objects.</summary>
public interface IPluginOverlayCanvas
{
    /// <summary>Queues text in screen coordinates.</summary>
    void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f);
    /// <summary>Queues a filled screen-space rectangle.</summary>
    void FillRectangle(float x, float y, float width, float height, PluginOverlayColor color);

    /// <summary>Draws an outlined screen-space rectangle using host-owned rendering state.</summary>
    void DrawRectangle(float x, float y, float width, float height, PluginOverlayColor color, float thickness = 1f);
    /// <summary>Queues a screen-space line.</summary>
    void DrawLine(float startX, float startY, float endX, float endY, PluginOverlayColor color, float thickness = 1f);
    /// <summary>Queues a host-approved asset by identifier.</summary>
    void DrawAsset(string approvedAssetId, float x, float y, float scale = 1f, PluginOverlayColor? tint = null);
    /// <summary>Queues a host-projected world marker.</summary>
    void DrawWorldMarker(float worldX, float worldY, string text, PluginOverlayColor color);

    /// <summary>Draws an outlined world-space rectangle after host-owned world-to-screen conversion.</summary>
    void DrawWorldRectangle(float worldX, float worldY, float width, float height, PluginOverlayColor color, float thickness = 1f);
}

/// <summary>Registers an overlay that is removed automatically with its plugin resource scope.</summary>
public interface IPluginOverlayService
{
    /// <summary>Registers a deterministic, scope-owned overlay callback.</summary>
    IPluginRegistration Register(PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw);
}
