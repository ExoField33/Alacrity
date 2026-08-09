using System;
using System.Diagnostics;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
        public static void DrawNotifications(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawNotifications(spriteBatch);
        }

        /// <summary>Draws host-validated diagnostics overlays without exposing mutable Terraria state to plugins.</summary>
        public static void DrawHitboxes(SpriteBatch spriteBatch)
        {
            DrawWorldOverlays(spriteBatch);
        }

        /// <summary>Dispatches framework-neutral plugin world overlays at Terraria's verified world UI phase.</summary>
        public static void DrawWorldOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawWorldOverlays(spriteBatch);
            _extensions?.Publish(new WorldOverlayRenderingEvent(CurrentPresentationTime));
        }

        /// <summary>Dispatches screen-space gameplay HUD overlays through Terraria's established UI SpriteBatch.</summary>
        public static void DrawHudOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawHudOverlays(spriteBatch);
            _extensions?.Publish(new HudRenderingEvent(CurrentPresentationTime));
        }

        /// <summary>Dispatches menu-space overlays from Terraria's menu SpriteBatch after version text is drawn.</summary>
        public static void DrawMenuOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawMenuOverlays(spriteBatch);
            _extensions?.Publish(new MenuRenderingEvent(CurrentPresentationTime));
        }

        /// <summary>Captures Terraria's already-computed melee collision rectangle for presentation on the next draw.</summary>
        public static void CaptureSwingHitbox(Player player, bool dontAttack, Rectangle hitbox)
        {
            CaptureMeleeCollisionBounds(player, dontAttack, hitbox);
        }

        /// <summary>Captures host-computed melee collision bounds for active generic presentation consumers.</summary>
        public static void CaptureMeleeCollisionBounds(Player player, bool dontAttack, Rectangle hitbox)
        {
            CombatPresentationRuntime.CaptureSwingHitbox(player, dontAttack, hitbox);
        }

        /// <summary>Compatibility entry point retained for the existing version-locked HUD patch.</summary>
        public static void DrawPlayerList(SpriteBatch spriteBatch)
        {
            DrawHudWidgets(spriteBatch);
        }

        /// <summary>Dispatches generic retained HUD widgets without knowing which plugin provided them.</summary>
        public static void DrawHudWidgets(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawHudWidgets(spriteBatch);
        }

        /// <summary>
        /// Appends verified plugin bindings to Terraria's native controls list. The controls adapter
        /// is deliberately optional: a changed UI signature leaves vanilla controls untouched.
        /// </summary>

        private static TimeSpan CurrentPresentationTime => TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
    }
}
