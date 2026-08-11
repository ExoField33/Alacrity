using System;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.GameState.World;

/// <summary>Pure section-window math used by the update-thread world-section capture.</summary>
internal readonly struct TerrariaWorldSectionBounds
{
    internal TerrariaWorldSectionBounds(int baseStartX, int baseStartY, int baseEndX, int baseEndY, int startX, int startY, int endX, int endY)
    {
        BaseStartX = baseStartX;
        BaseStartY = baseStartY;
        BaseEndX = baseEndX;
        BaseEndY = baseEndY;
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
    }

    internal int BaseStartX { get; }
    internal int BaseStartY { get; }
    internal int BaseEndX { get; }
    internal int BaseEndY { get; }
    internal int StartX { get; }
    internal int StartY { get; }
    internal int EndX { get; }
    internal int EndY { get; }

    internal static TerrariaWorldSectionBounds Calculate(
        float screenX,
        float screenY,
        int screenWidth,
        int screenHeight,
        float zoom,
        int maximumSectionX,
        int maximumSectionY,
        int margin,
        int sectionWidthPixels,
        int sectionHeightPixels)
    {
        ValidateMargin(margin);
        if (maximumSectionX < 0 || maximumSectionY < 0)
        {
            return new TerrariaWorldSectionBounds(0, 0, -1, -1, 0, 0, -1, -1);
        }

        float safeZoom = Math.Max(0.1f, zoom);
        int baseStartX = Math.Max(0, (int)Math.Floor(screenX / sectionWidthPixels));
        int baseStartY = Math.Max(0, (int)Math.Floor(screenY / sectionHeightPixels));
        int baseEndX = Math.Min(maximumSectionX, (int)Math.Floor((screenX + screenWidth / safeZoom) / sectionWidthPixels));
        int baseEndY = Math.Min(maximumSectionY, (int)Math.Floor((screenY + screenHeight / safeZoom) / sectionHeightPixels));
        return new TerrariaWorldSectionBounds(
            baseStartX,
            baseStartY,
            baseEndX,
            baseEndY,
            Math.Max(0, baseStartX - margin),
            Math.Max(0, baseStartY - margin),
            Math.Min(maximumSectionX, baseEndX + margin),
            Math.Min(maximumSectionY, baseEndY + margin));
    }

    internal static void ValidateMargin(int margin)
    {
        if (margin < 0 || margin > PluginWorldSectionLimits.MaximumMargin)
        {
            throw new ArgumentOutOfRangeException(
                nameof(margin),
                margin,
                "World section margin must be between zero and " + PluginWorldSectionLimits.MaximumMargin + ".");
        }
    }
}
