using System.Collections.Generic;

#pragma warning disable CS1591

namespace Alacrity.PluginSdk;

/// <summary>
/// Host-defined limits for bounded visible world-section snapshots. Plugins must not use a large
/// margin as a substitute for scanning the full world.
/// </summary>
public static class PluginWorldSectionLimits
{
    /// <summary>Largest supported section margin in each direction.</summary>
    public const int MaximumMargin = 8;
}

/// <summary>
/// Immutable client tile-section state captured at the host update boundary for a bounded visible
/// world region. Values are detached from Terraria and remain safe to read after capture.
/// </summary>
public readonly struct PluginWorldSectionSnapshot
{
    public PluginWorldSectionSnapshot(int sectionX, int sectionY, float worldX, float worldY, float worldWidth, float worldHeight, bool isLoaded)
    {
        SectionX = sectionX;
        SectionY = sectionY;
        WorldX = worldX;
        WorldY = worldY;
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        IsLoaded = isLoaded;
    }

    public int SectionX { get; }
    public int SectionY { get; }
    public float WorldX { get; }
    public float WorldY { get; }
    public float WorldWidth { get; }
    public float WorldHeight { get; }
    public bool IsLoaded { get; }
}

/// <summary>
/// Provides the latest detached section state captured by the host update boundary. Calls may
/// return the previous update's frame while a new frame is being captured. Values append to the
/// caller-provided collection; the service is activation-scoped and throws after plugin disable.
/// The first read lazily requests update-thread capture, while subsequent reads are safe from
/// worker threads because they only copy detached values.
/// </summary>
public interface IPluginWorldSectionService
{
    /// <summary>
    /// Appends visible section state to <paramref name="destination"/> plus the requested
    /// section margin from zero through <see cref="PluginWorldSectionLimits.MaximumMargin"/>.
    /// The first call requests capture for this activation; it does not read live game state from
    /// the caller's thread.
    /// </summary>
    void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int margin = 0);
}

#pragma warning restore CS1591
