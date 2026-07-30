using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace AlacrityTerraria
{
    // This renderer observes vanilla collision geometry only. It never changes collision, packets, or simulation.
    internal static class HitboxRuntime
    {
        private const uint SwingLifetimeTicks = 2;
        private const int WhipCullMargin = 1400;
        private static readonly List<Vector2> WhipPoints = new List<Vector2>(32);
        private static SwingHitbox[] _swingHitboxes = Array.Empty<SwingHitbox>();
        private static bool _whipRenderingAvailable = true;
        private static int _candidateOutlines;
        private static int _submittedOutlines;

        internal static HitboxDrawDiagnostic LastDrawDiagnostic { get; private set; }

        internal static void CaptureSwingHitbox(Player player, bool dontAttack, Rectangle hitbox)
        {
            if (player == null || dontAttack || hitbox.Width <= 0 || hitbox.Height <= 0 || !player.active || player.dead)
                return;

            int slot = player.whoAmI;
            if (slot < 0)
                return;
            EnsureSwingCapacity(slot + 1);
            _swingHitboxes[slot] = new SwingHitbox(hitbox, Main.GameUpdateCount, true);
        }

        internal static void Draw(SpriteBatch spriteBatch, HitboxOverlaySettingsSnapshot settings)
        {
            _candidateOutlines = 0;
            _submittedOutlines = 0;
            if (spriteBatch == null || settings == null || !settings.HasVisibleOverlays || Main.gameMenu)
            {
                LastDrawDiagnostic = new HitboxDrawDiagnostic(0, 0);
                return;
            }

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            CameraTransform camera = new CameraTransform();
            if (settings.ShowPlayerHitboxes)
                DrawPlayers(spriteBatch, pixel, camera, settings.PlayerColor);
            if (settings.ShowNpcHitboxes)
                DrawNpcs(spriteBatch, pixel, camera, settings.NpcColor);
            if (settings.ShowProjectileHitboxes && (settings.ShowFriendlyProjectiles || settings.ShowHostileProjectiles))
                DrawProjectiles(spriteBatch, pixel, camera, settings);
            if (settings.ShowSwingHitboxes)
                DrawSwings(spriteBatch, pixel, camera, settings.SwingColor);
            LastDrawDiagnostic = new HitboxDrawDiagnostic(_candidateOutlines, _submittedOutlines);
        }

        internal static void Reset()
        {
            if (_swingHitboxes.Length > 0)
                Array.Clear(_swingHitboxes, 0, _swingHitboxes.Length);
            WhipPoints.Clear();
        }

        private static void DrawPlayers(SpriteBatch spriteBatch, Texture2D pixel, CameraTransform camera, PluginColor color)
        {
            int count = Math.Min(Main.maxPlayers, Main.player == null ? 0 : Main.player.Length);
            for (int i = 0; i < count; i++)
            {
                Player player = Main.player[i];
                if (player != null && player.active && !player.dead)
                    DrawWorldRectangle(spriteBatch, pixel, camera, player.Hitbox, color, 210);
            }
        }

        private static void DrawNpcs(SpriteBatch spriteBatch, Texture2D pixel, CameraTransform camera, PluginColor color)
        {
            int count = Math.Min(Main.maxNPCs, Main.npc == null ? 0 : Main.npc.Length);
            for (int i = 0; i < count; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && npc.life > 0)
                    DrawWorldRectangle(spriteBatch, pixel, camera, npc.Hitbox, color, 210);
            }
        }

        private static void DrawProjectiles(SpriteBatch spriteBatch, Texture2D pixel, CameraTransform camera, HitboxOverlaySettingsSnapshot settings)
        {
            int count = Math.Min(Main.maxProjectiles, Main.projectile == null ? 0 : Main.projectile.Length);
            Rectangle expandedVisible = camera.VisibleWorld;
            expandedVisible.Inflate(WhipCullMargin, WhipCullMargin);
            for (int i = 0; i < count; i++)
            {
                Projectile projectile = Main.projectile[i];
                if (projectile == null || !projectile.active)
                    continue;

                bool friendly = projectile.friendly && !projectile.hostile;
                if (friendly)
                {
                    if (!settings.ShowFriendlyProjectiles)
                        continue;
                    DrawProjectile(spriteBatch, pixel, camera, projectile, settings.FriendlyProjectileColor, expandedVisible);
                }
                else if (projectile.hostile && settings.ShowHostileProjectiles)
                {
                    DrawProjectile(spriteBatch, pixel, camera, projectile, settings.HostileProjectileColor, expandedVisible);
                }
            }
        }

        private static void DrawProjectile(SpriteBatch spriteBatch, Texture2D pixel, CameraTransform camera, Projectile projectile, PluginColor color, Rectangle expandedVisible)
        {
            if (_whipRenderingAvailable && projectile.type >= 0 && projectile.type < ProjectileID.Sets.IsAWhip.Length && ProjectileID.Sets.IsAWhip[projectile.type])
            {
                if (!projectile.Hitbox.Intersects(expandedVisible))
                    return;
                try
                {
                    WhipPoints.Clear();
                    Projectile.FillWhipControlPoints(projectile, WhipPoints, null, true);
                    int width = Math.Max(1, projectile.Hitbox.Width);
                    int height = Math.Max(1, projectile.Hitbox.Height);
                    for (int pointIndex = 0; pointIndex < WhipPoints.Count; pointIndex++)
                    {
                        Vector2 point = WhipPoints[pointIndex];
                        Rectangle collisionPoint = new Rectangle((int)(point.X - width * 0.5f), (int)(point.Y - height * 0.5f), width, height);
                        DrawWorldRectangle(spriteBatch, pixel, camera, collisionPoint, color, 220);
                    }
                    return;
                }
                catch (Exception)
                {
                    // A version mismatch must degrade to the ordinary vanilla projectile bounds, not break drawing.
                    _whipRenderingAvailable = false;
                    WhipPoints.Clear();
                }
            }

            DrawWorldRectangle(spriteBatch, pixel, camera, projectile.Hitbox, color, 220);
        }

        private static void DrawSwings(SpriteBatch spriteBatch, Texture2D pixel, CameraTransform camera, PluginColor color)
        {
            uint currentTick = Main.GameUpdateCount;
            for (int i = 0; i < _swingHitboxes.Length; i++)
            {
                SwingHitbox entry = _swingHitboxes[i];
                if (!entry.Active || currentTick - entry.Tick > SwingLifetimeTicks)
                    continue;
                DrawWorldRectangle(spriteBatch, pixel, camera, entry.Bounds, color, 225);
            }
        }

        private static void DrawWorldRectangle(SpriteBatch spriteBatch, Texture2D pixel, CameraTransform camera, Rectangle worldBounds, PluginColor color, byte alpha)
        {
            _candidateOutlines++;
            if (worldBounds.Width <= 0 || worldBounds.Height <= 0 || !worldBounds.Intersects(camera.VisibleWorld))
                return;

            Rectangle screenBounds = camera.ToScreen(worldBounds);
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                return;
            const int thickness = 2;
            Color drawColor = new Color(color.Red, color.Green, color.Blue, alpha);
            spriteBatch.Draw(pixel, new Rectangle(screenBounds.X, screenBounds.Y, screenBounds.Width, thickness), drawColor);
            spriteBatch.Draw(pixel, new Rectangle(screenBounds.X, screenBounds.Bottom - thickness, screenBounds.Width, thickness), drawColor);
            spriteBatch.Draw(pixel, new Rectangle(screenBounds.X, screenBounds.Y, thickness, screenBounds.Height), drawColor);
            spriteBatch.Draw(pixel, new Rectangle(screenBounds.Right - thickness, screenBounds.Y, thickness, screenBounds.Height), drawColor);
            _submittedOutlines++;
        }

        private static void EnsureSwingCapacity(int required)
        {
            if (_swingHitboxes.Length >= required)
                return;
            int capacity = _swingHitboxes.Length == 0 ? 16 : _swingHitboxes.Length;
            while (capacity < required)
                capacity *= 2;
            Array.Resize(ref _swingHitboxes, capacity);
        }

        private readonly struct SwingHitbox
        {
            internal SwingHitbox(Rectangle bounds, uint tick, bool active) { Bounds = bounds; Tick = tick; Active = active; }
            internal Rectangle Bounds { get; }
            internal uint Tick { get; }
            internal bool Active { get; }
        }

        internal readonly struct HitboxDrawDiagnostic
        {
            internal HitboxDrawDiagnostic(int candidates, int submitted) { Candidates = candidates; Submitted = submitted; }
            internal int Candidates { get; }
            internal int Submitted { get; }
        }

        private readonly struct CameraTransform
        {
            public CameraTransform()
            {
                Vector2 scaledSize = Main.Camera.ScaledSize;
                if (scaledSize.X <= 0f || scaledSize.Y <= 0f)
                    scaledSize = new Vector2(Main.screenWidth, Main.screenHeight);
                Vector2 position = Main.Camera.ScaledPosition;
                VisibleWorld = new Rectangle((int)Math.Floor(position.X), (int)Math.Floor(position.Y), Math.Max(1, (int)Math.Ceiling(scaledSize.X)), Math.Max(1, (int)Math.Ceiling(scaledSize.Y)));
            }

            internal Rectangle VisibleWorld { get; }

            internal Rectangle ToScreen(Rectangle bounds)
            {
                // This world draw phase uses Terraria's established screen-position convention, as do
                // EmoteBubble and the former Enhancer overlays. The active GameViewMatrix applies zoom.
                int x = (int)Math.Round(bounds.X - Main.screenPosition.X);
                int y = (int)Math.Round(bounds.Y - Main.screenPosition.Y);
                int width = Math.Max(1, bounds.Width);
                int height = Math.Max(1, bounds.Height);
                return new Rectangle(x, y, width, height);
            }
        }
    }
}
