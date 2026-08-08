using Microsoft.Xna.Framework;
using Terraria;

namespace AlacrityTerraria.Rendering.Projection;

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

    /// <summary>
    /// Validates the live assumptions of the version-locked world-overlay hook. The hook already
    /// supplies a world-transformed SpriteBatch, so zoom and gravity are observed for diagnostics
    /// only and must not be folded into the translation a second time.
    /// </summary>
    internal static bool TryVerifyLiveState(out string diagnostic)
    {
        Vector2 zoom = Main.GameViewMatrix.Zoom;
        float gravity = Main.LocalPlayer == null ? 1f : Main.LocalPlayer.gravDir;
        var state = new TerrariaWorldProjectionState(Main.screenPosition.X, Main.screenPosition.Y, zoom.X, zoom.Y, gravity);
        return TerrariaWorldProjectionVerifier.TryVerify(state, out diagnostic);
    }
}
