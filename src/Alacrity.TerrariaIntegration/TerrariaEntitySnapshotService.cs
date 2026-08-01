using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;
using Terraria;
using Terraria.ID;

namespace AlacrityTerraria;

/// <summary>Reads Terraria entities only at the integration boundary and emits detached presentation values.</summary>
internal sealed class TerrariaEntitySnapshotService : IPluginEntitySnapshotService
{
    public void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        int players = Math.Min(Main.maxPlayers, Main.player == null ? 0 : Main.player.Length);
        for (int i = 0; i < players; i++)
        {
            Player player = Main.player[i];
            if (player != null && player.active && !player.dead)
                destination.Add(new PluginEntitySnapshot(PluginEntityKind.Player, i, player.Hitbox.X, player.Hitbox.Y, player.Hitbox.Width, player.Hitbox.Height));
        }
        int npcs = Math.Min(Main.maxNPCs, Main.npc == null ? 0 : Main.npc.Length);
        for (int i = 0; i < npcs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc != null && npc.active && npc.life > 0)
                destination.Add(new PluginEntitySnapshot(PluginEntityKind.Npc, i, npc.Hitbox.X, npc.Hitbox.Y, npc.Hitbox.Width, npc.Hitbox.Height));
        }
        int projectiles = Math.Min(Main.maxProjectiles, Main.projectile == null ? 0 : Main.projectile.Length);
        for (int i = 0; i < projectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile == null || !projectile.active) continue;
            bool friendly = (projectile.friendly && !projectile.hostile) || projectile.sentry ||
                (projectile.type >= 0 && projectile.type < ProjectileID.Sets.SentryShot.Length && ProjectileID.Sets.SentryShot[projectile.type]);
            if (!friendly && !projectile.hostile) continue;
            destination.Add(new PluginEntitySnapshot(PluginEntityKind.Projectile, i, projectile.Hitbox.X, projectile.Hitbox.Y, projectile.Hitbox.Width, projectile.Hitbox.Height, friendly, projectile.hostile));
        }
    }

    public void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        CombatPresentationRuntime.CopyMeleeHitboxes(destination);
    }
}
