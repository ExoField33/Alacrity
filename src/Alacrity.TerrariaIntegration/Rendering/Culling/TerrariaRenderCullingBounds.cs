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

    /// <summary>Returns true only when source camera position or scaled size changed.</summary>
    internal bool Update(float nextPositionX, float nextPositionY, float nextSizeX, float nextSizeY)
    {
        if (initialized != 0 && nextPositionX == positionX && nextPositionY == positionY &&
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
        return true;
    }

    internal bool IsVisible(float worldX, float worldY, int width, int height, int margin)
    {
        int entityLeft = (int)Math.Floor(worldX);
        int entityTop = (int)Math.Floor(worldY);
        int entityRight = entityLeft + Math.Max(1, width);
        int entityBottom = entityTop + Math.Max(1, height);
        return entityRight >= left - margin && entityLeft <= right + margin &&
            entityBottom >= top - margin && entityTop <= bottom + margin;
    }
}
