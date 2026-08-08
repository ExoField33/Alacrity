using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria.Rendering.Hud;

/// <summary>
/// Guards the version-locked player-head renderer. Terraria 1.4.5.6's renderer writes through
/// <see cref="Main.spriteBatch"/>, so invoking it for any other active batch could corrupt a later
/// HUD widget. Avatar rendering is therefore allowed only at the matching native batch boundary.
/// </summary>
internal static class TerrariaAvatarRenderBoundary
{
    internal static bool UsesActiveTerrariaBatch(SpriteBatch spriteBatch)
    {
        return UsesExpectedBatch(spriteBatch, Main.spriteBatch);
    }

    /// <summary>Pure comparison kept separate so the real XNA harness can verify it without bootstrapping Terraria.</summary>
    internal static bool UsesExpectedBatch(SpriteBatch spriteBatch, SpriteBatch expectedTerrariaBatch)
    {
        return spriteBatch != null && (expectedTerrariaBatch == null || ReferenceEquals(spriteBatch, expectedTerrariaBatch));
    }

    internal static bool TryDraw(SpriteBatch spriteBatch, Action draw)
    {
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        if (!UsesActiveTerrariaBatch(spriteBatch)) return false;
        draw();
        return true;
    }

    /// <summary>
    /// Runs Terraria's native player-head renderer in the UI batch configuration it expects. The
    /// renderer uses immediate mode to apply dye shaders; the caller's deferred UI batch is always
    /// restored before control returns so a head cannot corrupt later plugin or vanilla UI drawing.
    /// </summary>
    internal static bool TryDrawUiIsolated(SpriteBatch spriteBatch, Action draw)
    {
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        if (!UsesActiveTerrariaBatch(spriteBatch)) return false;

        bool deferredBatchRestored = false;
        try
        {
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, null, null, Main.UIScaleMatrix);
            try
            {
                draw();
            }
            finally
            {
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Main.UIScaleMatrix);
                deferredBatchRestored = true;
            }

            return true;
        }
        finally
        {
            // A failing native draw must still leave the shared UI renderer usable for later widgets.
            if (!deferredBatchRestored)
            {
                try { spriteBatch.End(); } catch { }
                try { spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Main.UIScaleMatrix); } catch { }
            }
        }
    }

    /// <summary>Testable form of the same native-batch contract.</summary>
    internal static bool TryDraw(SpriteBatch spriteBatch, SpriteBatch expectedTerrariaBatch, Action draw)
    {
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        if (!UsesExpectedBatch(spriteBatch, expectedTerrariaBatch)) return false;
        draw();
        return true;
    }
}
