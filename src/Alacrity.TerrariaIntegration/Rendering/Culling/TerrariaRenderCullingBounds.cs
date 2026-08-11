using System;

namespace AlacrityTerraria.Rendering.Culling;

/// <summary>Cached normalized world-camera rectangle used by allocation-free culling checks.</summary>
internal sealed class TerrariaRenderCullingBounds
{
    private int initialized;
    private float positionX;
    private float positionY;
    private float sizeX;
    private float sizeY;
    private int left;
    private int top;
    private int right;
    private int bottom;
    private int usable;

    /// <summary>Returns true only when source camera position or scaled size changed.</summary>
    internal bool Update(float nextPositionX, float nextPositionY, float nextSizeX, float nextSizeY)
    {
        if (!IsFinite(nextPositionX) || !IsFinite(nextPositionY) ||
            !IsFinite(nextSizeX) || !IsFinite(nextSizeY) ||
            nextSizeX < 0f || nextSizeY < 0f ||
            !CanConvertToInt(nextPositionX) || !CanConvertToInt(nextPositionY) ||
            !CanConvertToInt(nextPositionX + nextSizeX) || !CanConvertToInt(nextPositionY + nextSizeY))
        {
            usable = 0;
            initialized = 1;
            return true;
        }

        if (initialized != 0 && usable != 0 && nextPositionX == positionX && nextPositionY == positionY &&
            nextSizeX == sizeX && nextSizeY == sizeY)
        {
            return false;
        }

        positionX = nextPositionX;
        positionY = nextPositionY;
        sizeX = nextSizeX;
        sizeY = nextSizeY;
        left = (int)Math.Floor(nextPositionX);
        top = (int)Math.Floor(nextPositionY);
        right = (int)Math.Ceiling(nextPositionX + nextSizeX);
        bottom = (int)Math.Ceiling(nextPositionY + nextSizeY);
        initialized = 1;
        usable = 1;
        return true;
    }

    internal bool IsVisible(float worldX, float worldY, int width, int height, int margin)
    {
        if (usable == 0 || !IsFinite(worldX) || !IsFinite(worldY) || !CanConvertToInt(worldX) || !CanConvertToInt(worldY))
        {
            return true;
        }

        int entityLeft = (int)Math.Floor(worldX);
        int entityTop = (int)Math.Floor(worldY);
        long entityRight = (long)entityLeft + Math.Max(1, width);
        long entityBottom = (long)entityTop + Math.Max(1, height);
        return entityRight >= (long)left - margin && entityLeft <= (long)right + margin &&
            entityBottom >= (long)top - margin && entityTop <= (long)bottom + margin;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool CanConvertToInt(float value)
    {
        return value >= int.MinValue && value <= int.MaxValue;
    }
}
