using Microsoft.Xna.Framework;
using Terraria;

namespace AlacrityTerraria;

/// <summary>
/// Centralizes world-to-screen conversion for the verified world-overlay hook. That hook receives
/// an already screen-space SpriteBatch, so applying GameViewMatrix or UI scale here would transform
/// coordinates twice; Main.screenPosition is the only required translation for this draw phase.
/// </summary>
internal static class TerrariaWorldProjection
{
    internal static Vector2 Project(float worldX, float worldY)
    {
        TerrariaWorldProjectionMath.Project(worldX, worldY, Main.screenPosition.X, Main.screenPosition.Y, out float screenX, out float screenY);
        return new Vector2(screenX, screenY);
    }
}
