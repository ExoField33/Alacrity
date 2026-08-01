using System;
using System.Collections.Generic;
using System.Threading;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AlacrityTerraria;

/// <summary>
/// Shared update-phase entity cache. Terraria state is read only from the integration update thread;
/// consumers copy detached values from the atomically published frame and can safely read it elsewhere.
/// </summary>
internal sealed class TerrariaEntitySnapshotCache
{
    private readonly ReaderWriterLockSlim gate = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    private Snapshot current;
    private Snapshot alternate;
    private uint capturedTick = uint.MaxValue;

    internal TerrariaEntitySnapshotCache()
    {
        // Plugin bootstrap precedes Terraria.Main's full static initialization. Allocate only inert
        // placeholders here; the first established update capture sizes both frames from live state.
        current = new Snapshot(Array.Empty<PluginEntitySnapshot>(), Array.Empty<PluginEntitySnapshot>(), 0, 0, 0, 0);
        alternate = new Snapshot(Array.Empty<PluginEntitySnapshot>(), Array.Empty<PluginEntitySnapshot>(), 0, 0, 0, 0);
    }

    internal void CaptureForCurrentTick()
    {
        uint tick = Main.GameUpdateCount;
        if (tick == capturedTick) return;

        gate.EnterWriteLock();
        try
        {
            if (tick == capturedTick) return;
            Snapshot next = alternate;
            EnsureCapacity(next);
            CapturePlayers(next);
            int entityCount = CopyLiveEntities(next.Entities);
            int meleeCount = CombatPresentationRuntime.HasMeleeCaptureDemand ? CombatPresentationRuntime.CopyMeleeHitboxes(next.Melee) : 0;
            next.SetCounts(entityCount, meleeCount, tick);
            alternate = current;
            Volatile.Write(ref current, next);
            capturedTick = tick;
        }
        finally { gate.ExitWriteLock(); }
    }

    /// <summary>
    /// Refreshes only the local player's already-captured presentation bounds at the late world-draw
    /// boundary. Local movement can occur after the input-phase shared capture; a full entity rescan
    /// here would be needlessly expensive and make the renderer depend on live mutable state.
    /// </summary>
    internal void RefreshLocalPlayerPresentation()
    {
        if (capturedTick == uint.MaxValue || Main.player == null || Main.myPlayer < 0 || Main.myPlayer >= Main.player.Length)
            return;
        Player player = Main.player[Main.myPlayer];
        if (player == null || !player.active || player.dead)
            return;

        Rectangle hitbox = player.Hitbox;
        gate.EnterWriteLock();
        try
        {
            Snapshot snapshot = current;
            for (int index = 0; index < snapshot.EntityCount; index++)
            {
                PluginEntitySnapshot entity = snapshot.Entities[index];
                if (entity.Kind != PluginEntityKind.Player || entity.Id != Main.myPlayer)
                    continue;
                snapshot.Entities[index] = new PluginEntitySnapshot(PluginEntityKind.Player, Main.myPlayer, hitbox.X, hitbox.Y, hitbox.Width, hitbox.Height);
                return;
            }
        }
        finally { gate.ExitWriteLock(); }
    }

    internal IPluginEntitySnapshotService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (!HasReadAccess(manifest))
            return new DeniedService(manifest.Id);
        var guard = new ScopeGuard(resources);
        resources.Own("entity-snapshots", PluginResourceKind.EventSubscription, guard);
        return new ScopedService(this, guard);
    }

    internal IPluginPlayerService CreatePlayerService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (!HasReadAccess(manifest)) return new DeniedPlayerService(manifest.Id);
        var guard = new ScopeGuard(resources);
        resources.Own("player-snapshots", PluginResourceKind.EventSubscription, guard);
        return new ScopedPlayerService(this, guard);
    }

    private static bool HasReadAccess(PluginManifest manifest)
    {
        return (manifest.Capabilities & PluginCapability.GameStateRead) != 0 && (manifest.Permissions & PluginPermission.ReadGameState) != 0;
    }

    private static int CopyLiveEntities(PluginEntitySnapshot[] destination)
    {
        int count = 0;
        int players = Math.Min(Main.maxPlayers, Main.player == null ? 0 : Main.player.Length);
        for (int i = 0; i < players && count < destination.Length; i++)
        {
            Player player = Main.player[i];
            if (player != null && player.active && !player.dead)
                destination[count++] = new PluginEntitySnapshot(PluginEntityKind.Player, i, player.Hitbox.X, player.Hitbox.Y, player.Hitbox.Width, player.Hitbox.Height);
        }
        int npcs = Math.Min(Main.maxNPCs, Main.npc == null ? 0 : Main.npc.Length);
        for (int i = 0; i < npcs && count < destination.Length; i++)
        {
            NPC npc = Main.npc[i];
            if (npc != null && npc.active && npc.life > 0)
                destination[count++] = new PluginEntitySnapshot(PluginEntityKind.Npc, i, npc.Hitbox.X, npc.Hitbox.Y, npc.Hitbox.Width, npc.Hitbox.Height);
        }
        int projectiles = Math.Min(Main.maxProjectiles, Main.projectile == null ? 0 : Main.projectile.Length);
        for (int i = 0; i < projectiles && count < destination.Length; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile == null || !projectile.active) continue;
            bool friendly = (projectile.friendly && !projectile.hostile) || projectile.sentry ||
                (projectile.type >= 0 && projectile.type < ProjectileID.Sets.SentryShot.Length && ProjectileID.Sets.SentryShot[projectile.type]);
            destination[count++] = new PluginEntitySnapshot(PluginEntityKind.Projectile, i, projectile.Hitbox.X, projectile.Hitbox.Y, projectile.Hitbox.Width, projectile.Hitbox.Height, friendly, projectile.hostile);
        }
        return count;
    }

    private static void EnsureCapacity(Snapshot snapshot)
    {
        int entityCapacity = Math.Max(1, Main.maxPlayers + Main.maxNPCs + Main.maxProjectiles);
        int meleeCapacity = Math.Max(1, Main.maxPlayers);
        if (snapshot.Entities.Length < entityCapacity) Array.Resize(ref snapshot.Entities, entityCapacity);
        if (snapshot.Melee.Length < meleeCapacity) Array.Resize(ref snapshot.Melee, meleeCapacity);
        if (snapshot.Players.Length < Main.maxPlayers) snapshot.ResizePlayers(Main.maxPlayers);
    }

    private static void CapturePlayers(Snapshot snapshot)
    {
        Player[] players = Main.player;
        int count = Math.Min(Main.maxPlayers, players == null ? 0 : players.Length);
        for (int index = 0; index < snapshot.Players.Length; index++)
        {
            if (index >= count || players[index] == null || (!players[index].active && !players[index].ghost))
            {
                snapshot.Players[index] = default;
                snapshot.BuffCounts[index] = 0;
                continue;
            }
            Player player = players[index];
            snapshot.Players[index] = new PluginPlayerSnapshot(index, player.name, player.team, player.active, player.dead, player.ghost, player.statLife, player.statLifeMax, player.respawnTimer, IsSuspectedBot(player));
            int limit = Math.Min(player.buffType == null ? 0 : player.buffType.Length, player.buffTime == null ? 0 : player.buffTime.Length);
            if (snapshot.Buffs[index] == null || snapshot.Buffs[index].Length < limit) snapshot.Buffs[index] = new PluginBuffSnapshot[Math.Max(1, limit)];
            int buffs = 0;
            for (int buffIndex = 0; buffIndex < limit; buffIndex++)
                if (player.buffType[buffIndex] > 0) snapshot.Buffs[index][buffs++] = new PluginBuffSnapshot(player.buffType[buffIndex], player.buffTime[buffIndex]);
            snapshot.BuffCounts[index] = buffs;
        }
    }

    // This stays in the integration layer because it reads equipment arrays. Plugins receive only
    // the immutable heuristic result and never a mutable Terraria.Player reference.
    private static bool IsSuspectedBot(Player player)
    {
        if (string.IsNullOrWhiteSpace(player.name)) return true;
        const int markerItemId = 3015;
        int markers = CountMarker(player.armor, markerItemId) + CountMarker(player.miscEquips, markerItemId) + CountMarker(player.dye, markerItemId) + CountMarker(player.miscDyes, markerItemId);
        return markers >= 3;
    }

    private static int CountMarker(Item[] items, int markerItemId)
    {
        if (items == null) return 0;
        int count = 0;
        for (int index = 0; index < items.Length; index++)
            if (items[index] != null && items[index].type == markerItemId) count++;
        return count;
    }

    private sealed class ScopedService : IPluginEntitySnapshotService, IPluginMeleeCollisionSnapshotService
    {
        private readonly TerrariaEntitySnapshotCache cache;
        private readonly ScopeGuard guard;
        public ScopedService(TerrariaEntitySnapshotCache cache, ScopeGuard guard) { this.cache = cache; this.guard = guard; }
        public void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination) => Copy(destination, false);
        public void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination) => Copy(destination, true);
        public IPluginRegistration RequestMeleeCollisionSnapshots()
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginEntitySnapshotService", "The owning plugin scope has been released.");
            IDisposable demand = CombatPresentationRuntime.AcquireMeleeCaptureDemand();
            var registration = new DemandRegistration(demand);
            guard.Resources.Own(registration.Name, PluginResourceKind.EventSubscription, registration);
            return registration;
        }
        private void Copy(ICollection<PluginEntitySnapshot> destination, bool melee)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginEntitySnapshotService", "The owning plugin scope has been released.");
            cache.gate.EnterReadLock();
            try
            {
            Snapshot snapshot = cache.current;
            PluginEntitySnapshot[] source = melee ? snapshot.Melee : snapshot.Entities;
            int count = melee ? snapshot.MeleeCount : snapshot.EntityCount;
            for (int index = 0; index < count; index++) destination.Add(source[index]);
            }
            finally { cache.gate.ExitReadLock(); }
        }
    }

    private sealed class ScopeGuard : IPluginRegistration
    {
        internal ScopeGuard(IPluginResourceScope resources) { Resources = resources; }
        internal IPluginResourceScope Resources { get; }
        public string Name => "entity-snapshots";
        public bool IsReleased { get; private set; }
        public void Dispose() { IsReleased = true; }
    }

    private sealed class ScopedPlayerService : IPluginPlayerService
    {
        private readonly TerrariaEntitySnapshotCache cache; private readonly ScopeGuard guard;
        internal ScopedPlayerService(TerrariaEntitySnapshotCache cache, ScopeGuard guard) { this.cache = cache; this.guard = guard; }
        public bool TryGet(int playerId, out PluginPlayerSnapshot player)
        {
            EnsureActive(); player = default;
            cache.gate.EnterReadLock(); try { if (playerId < 0 || playerId >= cache.current.Players.Length) return false; player = cache.current.Players[playerId]; return player.IsActive || player.IsGhost; } finally { cache.gate.ExitReadLock(); }
        }
        public string GetName(int playerId) => TryGet(playerId, out PluginPlayerSnapshot player) ? player.Name : null;
        public void CopyPlayers(ICollection<PluginPlayerSnapshot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination)); EnsureActive();
            cache.gate.EnterReadLock(); try { PluginPlayerSnapshot[] players = cache.current.Players; for (int index = 0; index < players.Length; index++) if (players[index].IsActive || players[index].IsGhost) destination.Add(players[index]); } finally { cache.gate.ExitReadLock(); }
        }
        public void CopyBuffs(int playerId, ICollection<PluginBuffSnapshot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination)); EnsureActive();
            cache.gate.EnterReadLock(); try { if (playerId < 0 || playerId >= cache.current.Buffs.Length) return; PluginBuffSnapshot[] buffs = cache.current.Buffs[playerId]; int count = cache.current.BuffCounts[playerId]; for (int index = 0; buffs != null && index < count; index++) destination.Add(buffs[index]); } finally { cache.gate.ExitReadLock(); }
        }
        private void EnsureActive() { if (guard.IsReleased) throw new ObjectDisposedException("IPluginPlayerService"); }
    }

    private sealed class DeniedPlayerService : IPluginPlayerService
    {
        private readonly PluginId owner; internal DeniedPlayerService(PluginId owner) { this.owner = owner; }
        public bool TryGet(int playerId, out PluginPlayerSnapshot player) { player = default; Deny(); return false; }
        public string GetName(int playerId) { Deny(); return null; }
        public void CopyPlayers(ICollection<PluginPlayerSnapshot> destination) { Deny(); }
        public void CopyBuffs(int playerId, ICollection<PluginBuffSnapshot> destination) { Deny(); }
        private void Deny() => throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare GameStateRead capability and ReadGameState permission before reading player snapshots.");
    }

    private sealed class DeniedService : IPluginEntitySnapshotService, IPluginMeleeCollisionSnapshotService
    {
        private readonly PluginId owner;
        internal DeniedService(PluginId owner) { this.owner = owner; }
        public void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination) => Deny();
        public void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination) => Deny();
        public IPluginRegistration RequestMeleeCollisionSnapshots() { Deny(); return null; }
        private void Deny() => throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare GameStateRead capability and ReadGameState permission before reading entity snapshots.");
    }

    private sealed class Snapshot
    {
        internal Snapshot(PluginEntitySnapshot[] entities, PluginEntitySnapshot[] melee, int playerCapacity, int entityCount, int meleeCount, uint tick) { Entities = entities; Melee = melee; Players = new PluginPlayerSnapshot[playerCapacity]; Buffs = new PluginBuffSnapshot[playerCapacity][]; BuffCounts = new int[playerCapacity]; SetCounts(entityCount, meleeCount, tick); }
        internal PluginEntitySnapshot[] Entities;
        internal PluginEntitySnapshot[] Melee;
        internal PluginPlayerSnapshot[] Players;
        internal PluginBuffSnapshot[][] Buffs;
        internal int[] BuffCounts;
        internal int EntityCount { get; private set; }
        internal int MeleeCount { get; private set; }
        internal uint Tick { get; private set; }
        internal void SetCounts(int entityCount, int meleeCount, uint tick) { EntityCount = entityCount; MeleeCount = meleeCount; Tick = tick; }
        internal void ResizePlayers(int capacity) { Array.Resize(ref Players, capacity); Array.Resize(ref Buffs, capacity); Array.Resize(ref BuffCounts, capacity); }
    }

    private sealed class DemandRegistration : IPluginRegistration
    {
        private readonly IDisposable demand;
        private bool released;
        internal DemandRegistration(IDisposable demand) { this.demand = demand; }
        public string Name => "melee-collision-snapshots";
        public bool IsReleased => released;
        public void Dispose() { if (released) return; released = true; demand.Dispose(); }
    }
}
