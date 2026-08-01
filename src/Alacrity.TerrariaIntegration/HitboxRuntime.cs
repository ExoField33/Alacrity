using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Terraria;

namespace AlacrityTerraria
{
    // This is a core, version-sensitive capture point. It retains only vanilla-computed values;
    // bundled diagnostics decide later whether and how those values are rendered.
    internal static class CombatPresentationRuntime
    {
        private const uint SwingLifetimeTicks = 2;
        private static SwingHitbox[] _swingHitboxes = Array.Empty<SwingHitbox>();

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

        internal static void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination)
        {
            uint currentTick = Main.GameUpdateCount;
            for (int i = 0; i < _swingHitboxes.Length; i++)
            {
                SwingHitbox entry = _swingHitboxes[i];
                if (!entry.Active || currentTick - entry.Tick > SwingLifetimeTicks)
                    continue;
                destination.Add(new PluginEntitySnapshot(PluginEntityKind.MeleeHitbox, i, entry.Bounds.X, entry.Bounds.Y, entry.Bounds.Width, entry.Bounds.Height));
            }
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


    }
}
