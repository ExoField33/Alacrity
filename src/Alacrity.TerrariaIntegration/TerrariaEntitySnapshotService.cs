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
    private int entityDemand;
    private int playerDemand;
    private int buffDemand;
    private int botClassificationDemand;
    private int botClassificationRefreshRequested;
    private bool multiplayerSessionActive;
    private uint botClassificationDueTick = uint.MaxValue;
    private bool[] suspectedBots = Array.Empty<bool>();
    private bool[] botClassificationKnown = Array.Empty<bool>();
    private string[] botClassificationNames = Array.Empty<string>();

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
        int entitiesRequested = Volatile.Read(ref entityDemand);
        int playersRequested = Volatile.Read(ref playerDemand);
        int buffsRequested = Volatile.Read(ref buffDemand);
        int botsRequested = Volatile.Read(ref botClassificationDemand);
        bool meleeRequested = CombatPresentationRuntime.HasMeleeCaptureDemand;
        bool refreshBotClassification = botsRequested != 0 && ShouldRefreshBotClassification(tick);
        if (entitiesRequested == 0 && playersRequested == 0 && buffsRequested == 0 && botsRequested == 0 && !meleeRequested)
            return;

        gate.EnterWriteLock();
        try
        {
            if (tick == capturedTick) return;
            Snapshot next = alternate;
            EnsureCapacity(next);
            if (playersRequested != 0 || buffsRequested != 0 || refreshBotClassification)
                CapturePlayers(next, buffsRequested != 0, botsRequested != 0, refreshBotClassification);
            else
                next.ClearPlayers();
            int entityCount = entitiesRequested != 0 ? CopyLiveEntities(next.Entities) : 0;
            int meleeCount = meleeRequested ? CombatPresentationRuntime.CopyMeleeHitboxes(next.Melee) : 0;
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
        try
        {
            resources.Own("entity-snapshots", PluginResourceKind.EventSubscription, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }
        return new ScopedService(this, guard);
    }

    internal IPluginPlayerService CreatePlayerService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (!HasReadAccess(manifest)) return new DeniedPlayerService(manifest.Id);
        var guard = new ScopeGuard(resources);
        try
        {
            resources.Own("player-snapshots", PluginResourceKind.EventSubscription, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }
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

    private void EnsureCapacity(Snapshot snapshot)
    {
        int entityCapacity = Math.Max(1, Main.maxPlayers + Main.maxNPCs + Main.maxProjectiles);
        int meleeCapacity = Math.Max(1, Main.maxPlayers);
        if (snapshot.Entities.Length < entityCapacity) Array.Resize(ref snapshot.Entities, entityCapacity);
        if (snapshot.Melee.Length < meleeCapacity) Array.Resize(ref snapshot.Melee, meleeCapacity);
        if (snapshot.Players.Length < Main.maxPlayers) snapshot.ResizePlayers(Main.maxPlayers);
        if (suspectedBots.Length < Main.maxPlayers)
        {
            Array.Resize(ref suspectedBots, Main.maxPlayers);
            Array.Resize(ref botClassificationKnown, Main.maxPlayers);
            Array.Resize(ref botClassificationNames, Main.maxPlayers);
        }
    }

    private bool ShouldRefreshBotClassification(uint tick)
    {
        bool connected = Main.netMode == 1 && !Main.gameMenu;
        if (!connected)
        {
            multiplayerSessionActive = false;
            botClassificationDueTick = uint.MaxValue;
            Interlocked.Exchange(ref botClassificationRefreshRequested, 0);
            return false;
        }

        if (!multiplayerSessionActive)
        {
            multiplayerSessionActive = true;
            botClassificationDueTick = unchecked(tick + 120U);
        }

        if (Interlocked.Exchange(ref botClassificationRefreshRequested, 0) != 0)
        {
            botClassificationDueTick = uint.MaxValue;
            return true;
        }

        if (botClassificationDueTick != uint.MaxValue && unchecked(tick - botClassificationDueTick) < 0x80000000U)
        {
            botClassificationDueTick = uint.MaxValue;
            return true;
        }

        return false;
    }

    private void CapturePlayers(Snapshot snapshot, bool includeBuffs, bool includeBotClassification, bool refreshBotClassification)
    {
        Player[] players = Main.player;
        int count = Math.Min(Main.maxPlayers, players == null ? 0 : players.Length);
        for (int index = 0; index < snapshot.Players.Length; index++)
        {
            if (index >= count || players[index] == null || (!players[index].active && !players[index].ghost))
            {
                snapshot.Players[index] = default;
                snapshot.BuffCounts[index] = 0;
                if (index < botClassificationKnown.Length)
                {
                    botClassificationKnown[index] = false;
                    suspectedBots[index] = false;
                    botClassificationNames[index] = null;
                }
                continue;
            }
            Player player = players[index];
            if (includeBotClassification && refreshBotClassification)
            {
                string name = player.name ?? string.Empty;
                suspectedBots[index] = IsLikelyBotPlayer(player, name);
                botClassificationNames[index] = name;
                botClassificationKnown[index] = true;
            }
            snapshot.Players[index] = new PluginPlayerSnapshot(index, player.name, player.team, player.active, player.dead, player.ghost, player.statLife, player.statLifeMax, player.respawnTimer, includeBotClassification && suspectedBots[index]);
            if (!includeBuffs)
            {
                snapshot.BuffCounts[index] = 0;
                continue;
            }
            int limit = Math.Min(player.buffType == null ? 0 : player.buffType.Length, player.buffTime == null ? 0 : player.buffTime.Length);
            if (snapshot.Buffs[index] == null || snapshot.Buffs[index].Length < limit) snapshot.Buffs[index] = new PluginBuffSnapshot[Math.Max(1, limit)];
            int buffs = 0;
            for (int buffIndex = 0; buffIndex < limit; buffIndex++)
                if (player.buffType[buffIndex] > 0) snapshot.Buffs[index][buffs++] = new PluginBuffSnapshot(player.buffType[buffIndex], player.buffTime[buffIndex]);
            snapshot.BuffCounts[index] = buffs;
        }
    }

    // This is intentionally called only after the initial two-second multiplayer delay or an explicit
    // Player List refresh request. Inventory inspection is never part of the normal snapshot hot path.
    private static bool IsLikelyBotPlayer(Player player, string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return true;
        return CountItemType(player.armor, 3015) >= 3 || CountItemType(player.miscEquips, 3015) >= 3;
    }

    private static int CountItemType(Item[] items, int type)
    {
        if (items == null) return 0;
        int count = 0;
        for (int index = 0; index < items.Length; index++)
            if (items[index] != null && items[index].type == type) count++;
        return count;
    }

    private sealed class ScopedService : IPluginEntitySnapshotService, IPluginMeleeCollisionSnapshotService
    {
        private readonly TerrariaEntitySnapshotCache cache;
        private readonly ScopeGuard guard;
        private IDisposable entityDemand;
        public ScopedService(TerrariaEntitySnapshotCache cache, ScopeGuard guard) { this.cache = cache; this.guard = guard; }
        public void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination) => Copy(destination, false);
        public void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination) => Copy(destination, true);
        public IPluginRegistration RequestMeleeCollisionSnapshots()
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginEntitySnapshotService", "The owning plugin scope has been released.");
            IDisposable demand = CombatPresentationRuntime.AcquireMeleeCaptureDemand();
            var registration = new DemandRegistration(demand);
            try { guard.Resources.Own(registration.Name, PluginResourceKind.EventSubscription, registration); }
            catch { registration.Dispose(); throw; }
            return registration;
        }
        private void Copy(ICollection<PluginEntitySnapshot> destination, bool melee)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginEntitySnapshotService", "The owning plugin scope has been released.");
            if (!melee) EnsureEntityDemand();
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
        private void EnsureEntityDemand()
        {
            if (entityDemand != null) return;
            entityDemand = cache.AcquireDemand(DemandKind.Entities);
            guard.OwnDemand(entityDemand);
        }
    }

    private sealed class ScopeGuard : IPluginRegistration
    {
        private readonly List<IDisposable> demands = new List<IDisposable>();
        internal ScopeGuard(IPluginResourceScope resources) { Resources = resources; }
        internal IPluginResourceScope Resources { get; }
        public string Name => "entity-snapshots";
        public bool IsReleased { get; private set; }
        internal void OwnDemand(IDisposable demand)
        {
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            if (IsReleased) { demand.Dispose(); throw new ObjectDisposedException("IPluginResourceScope"); }
            demands.Add(demand);
        }
        public void Dispose()
        {
            if (IsReleased) return;
            IsReleased = true;
            for (int index = demands.Count - 1; index >= 0; index--) demands[index].Dispose();
            demands.Clear();
        }
    }

    private IDisposable AcquireDemand(DemandKind kind)
    {
        switch (kind)
        {
            case DemandKind.Entities: Interlocked.Increment(ref entityDemand); break;
            case DemandKind.Players: Interlocked.Increment(ref playerDemand); break;
            case DemandKind.Buffs: Interlocked.Increment(ref buffDemand); break;
            case DemandKind.BotClassification: Interlocked.Increment(ref botClassificationDemand); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return new CounterDemand(this, kind);
    }

    private void ReleaseDemand(DemandKind kind)
    {
        switch (kind)
        {
            case DemandKind.Entities: Interlocked.Decrement(ref entityDemand); break;
            case DemandKind.Players: Interlocked.Decrement(ref playerDemand); break;
            case DemandKind.Buffs: Interlocked.Decrement(ref buffDemand); break;
            case DemandKind.BotClassification: Interlocked.Decrement(ref botClassificationDemand); break;
        }
    }

    private sealed class ScopedPlayerService : IPluginPlayerService, IPluginPlayerSnapshotDemandService
    {
        private readonly TerrariaEntitySnapshotCache cache; private readonly ScopeGuard guard;
        private IDisposable playerDemand;
        private IDisposable buffDemand;
        internal ScopedPlayerService(TerrariaEntitySnapshotCache cache, ScopeGuard guard) { this.cache = cache; this.guard = guard; }
        public bool TryGet(int playerId, out PluginPlayerSnapshot player)
        {
            EnsureActive(); player = default;
            EnsurePlayerDemand();
            cache.gate.EnterReadLock(); try { if (playerId < 0 || playerId >= cache.current.Players.Length) return false; player = cache.current.Players[playerId]; return player.IsActive || player.IsGhost; } finally { cache.gate.ExitReadLock(); }
        }
        public string GetName(int playerId) => TryGet(playerId, out PluginPlayerSnapshot player) ? player.Name : null;
        public void CopyPlayers(ICollection<PluginPlayerSnapshot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination)); EnsureActive();
            EnsurePlayerDemand();
            cache.gate.EnterReadLock(); try { PluginPlayerSnapshot[] players = cache.current.Players; for (int index = 0; index < players.Length; index++) if (players[index].IsActive || players[index].IsGhost) destination.Add(players[index]); } finally { cache.gate.ExitReadLock(); }
        }
        public void CopyBuffs(int playerId, ICollection<PluginBuffSnapshot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination)); EnsureActive();
            EnsurePlayerDemand();
            if (buffDemand == null) { buffDemand = cache.AcquireDemand(DemandKind.Buffs); guard.OwnDemand(buffDemand); }
            cache.gate.EnterReadLock(); try { if (playerId < 0 || playerId >= cache.current.Buffs.Length) return; PluginBuffSnapshot[] buffs = cache.current.Buffs[playerId]; int count = cache.current.BuffCounts[playerId]; for (int index = 0; buffs != null && index < count; index++) destination.Add(buffs[index]); } finally { cache.gate.ExitReadLock(); }
        }
        public IPluginRegistration RequestPlayerBuffSnapshots() => RequestBuffDemand();
        public IPluginRegistration RequestSuspectedBotClassification() => RequestBotDemand();
        private IPluginRegistration RequestBuffDemand()
        {
            EnsureActive();
            var registration = new DemandRegistration(cache.AcquireDemand(DemandKind.Buffs), "player-buffs");
            try { guard.Resources.Own(registration.Name, PluginResourceKind.EventSubscription, registration); }
            catch { registration.Dispose(); throw; }
            return registration;
        }
        private void EnsurePlayerDemand()
        {
            if (playerDemand != null) return;
            playerDemand = cache.AcquireDemand(DemandKind.Players);
            guard.OwnDemand(playerDemand);
        }
        private IPluginRegistration RequestBotDemand()
        {
            EnsureActive();
            var registration = new DemandRegistration(cache.AcquireDemand(DemandKind.BotClassification), "suspected-bots");
            try { guard.Resources.Own(registration.Name, PluginResourceKind.EventSubscription, registration); }
            catch { registration.Dispose(); throw; }
            return registration;
        }
        public void RefreshSuspectedBotClassification()
        {
            EnsureActive();
            Interlocked.Exchange(ref cache.botClassificationRefreshRequested, 1);
        }
        private void EnsureActive() { if (guard.IsReleased) throw new ObjectDisposedException("IPluginPlayerService"); }
    }

    private sealed class DeniedPlayerService : IPluginPlayerService, IPluginPlayerSnapshotDemandService
    {
        private readonly PluginId owner; internal DeniedPlayerService(PluginId owner) { this.owner = owner; }
        public bool TryGet(int playerId, out PluginPlayerSnapshot player) { player = default; Deny(); return false; }
        public string GetName(int playerId) { Deny(); return null; }
        public void CopyPlayers(ICollection<PluginPlayerSnapshot> destination) { Deny(); }
        public void CopyBuffs(int playerId, ICollection<PluginBuffSnapshot> destination) { Deny(); }
        public IPluginRegistration RequestPlayerBuffSnapshots() { Deny(); return null; }
        public IPluginRegistration RequestSuspectedBotClassification() { Deny(); return null; }
        public void RefreshSuspectedBotClassification() { Deny(); }
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
        internal void ClearPlayers()
        {
            Array.Clear(Players, 0, Players.Length);
            Array.Clear(BuffCounts, 0, BuffCounts.Length);
        }
    }

    private sealed class DemandRegistration : IPluginRegistration
    {
        private readonly IDisposable demand;
        private bool released;
        internal DemandRegistration(IDisposable demand, string name = "melee-collision-snapshots") { this.demand = demand; Name = name; }
        public string Name { get; }
        public bool IsReleased => released;
        public void Dispose() { if (released) return; released = true; demand.Dispose(); }
    }

    private enum DemandKind { Entities, Players, Buffs, BotClassification }

    private sealed class CounterDemand : IDisposable
    {
        private TerrariaEntitySnapshotCache cache;
        private readonly DemandKind kind;
        internal CounterDemand(TerrariaEntitySnapshotCache cache, DemandKind kind) { this.cache = cache; this.kind = kind; }
        public void Dispose()
        {
            TerrariaEntitySnapshotCache current = Interlocked.Exchange(ref cache, null);
            if (current != null) current.ReleaseDemand(kind);
        }
    }
}
