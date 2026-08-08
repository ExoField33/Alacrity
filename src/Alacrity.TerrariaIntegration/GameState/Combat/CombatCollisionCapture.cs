using System;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Terraria;

namespace AlacrityTerraria.GameState.Combat
{
    /// <summary>
    /// Version-sensitive capture of Terraria's already-computed melee collision bounds. This is an
    /// integration capability, not a Hitboxes feature: any permitted presentation plugin can request
    /// the detached snapshots through <see cref="IPluginMeleeCollisionSnapshotService"/>.
    /// </summary>
    internal static class CombatPresentationRuntime
    {
        private const uint SwingLifetimeTicks = 2;
        private static SwingHitbox[] swingHitboxes = Array.Empty<SwingHitbox>();
        private static int meleeCaptureDemand;

        internal static bool HasMeleeCaptureDemand => System.Threading.Volatile.Read(ref meleeCaptureDemand) > 0;

        internal static void CaptureSwingHitbox(Player player, bool dontAttack, Rectangle hitbox)
        {
            if (!HasMeleeCaptureDemand)
                return;
            if (player == null || dontAttack || hitbox.Width <= 0 || hitbox.Height <= 0 || !player.active || player.dead)
                return;

            int slot = player.whoAmI;
            if (slot < 0)
                return;
            EnsureSwingCapacity(slot + 1);
            swingHitboxes[slot] = new SwingHitbox(hitbox, Main.GameUpdateCount, true);
        }

        internal static int CopyMeleeHitboxes(PluginEntitySnapshot[] destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            uint currentTick = Main.GameUpdateCount;
            int count = 0;
            for (int index = 0; index < swingHitboxes.Length && count < destination.Length; index++)
            {
                SwingHitbox entry = swingHitboxes[index];
                if (!entry.Active || currentTick - entry.Tick > SwingLifetimeTicks)
                    continue;
                destination[count++] = new PluginEntitySnapshot(PluginEntityKind.MeleeHitbox, index, entry.Bounds.X, entry.Bounds.Y, entry.Bounds.Width, entry.Bounds.Height);
            }
            return count;
        }

        internal static IDisposable AcquireMeleeCaptureDemand()
        {
            System.Threading.Interlocked.Increment(ref meleeCaptureDemand);
            return new CaptureDemandRegistration();
        }

        private static void EnsureSwingCapacity(int required)
        {
            if (swingHitboxes.Length >= required)
                return;
            int capacity = swingHitboxes.Length == 0 ? 16 : swingHitboxes.Length;
            while (capacity < required)
                capacity *= 2;
            Array.Resize(ref swingHitboxes, capacity);
        }

        private readonly struct SwingHitbox
        {
            internal SwingHitbox(Rectangle bounds, uint tick, bool active) { Bounds = bounds; Tick = tick; Active = active; }
            internal Rectangle Bounds { get; }
            internal uint Tick { get; }
            internal bool Active { get; }
        }

        private sealed class CaptureDemandRegistration : IDisposable
        {
            private int released;
            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref released, 1) == 0)
                    System.Threading.Interlocked.Decrement(ref meleeCaptureDemand);
            }
        }
    }
}
