using System.Collections.Generic;

#pragma warning disable CS1591

namespace Alacrity.PluginSdk;

/// <summary>Immutable client tile-section state for a bounded visible world region.</summary>
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
/// Provides detached section state for the visible world region. The host bounds collection to the
/// caller's requested margin; plugins never receive the full mutable Terraria section grid.
/// </summary>
public interface IPluginWorldSectionService
{
    /// <summary>Appends visible section state plus the requested non-negative section margin.</summary>
    void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int margin = 0);
}

#pragma warning restore CS1591
