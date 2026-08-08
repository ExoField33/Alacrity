using System;
using System.Collections.Generic;

#pragma warning disable CS1591

namespace Alacrity.PluginSdk;

/// <summary>Presentation-only entity categories supplied by a supported Terraria integration.</summary>
public enum PluginEntityKind
{
    Player,
    Npc,
    Projectile,
    MeleeHitbox
}

/// <summary>
/// Stable identity for one captured entity lifetime. A new occupant of the same Terraria slot
/// receives a different generation, so a stale handle cannot resolve to its replacement.
/// </summary>
public readonly struct PluginEntityHandle : IEquatable<PluginEntityHandle>
{
    /// <summary>Creates a generation-aware entity identity.</summary>
    public PluginEntityHandle(PluginEntityKind kind, int slot, uint generation)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        Kind = kind; Slot = slot; Generation = generation;
    }
    /// <summary>Entity category.</summary>
    public PluginEntityKind Kind { get; }
    /// <summary>Native Terraria slot, meaningful only with <see cref="Generation"/>.</summary>
    public int Slot { get; }
    /// <summary>Host-owned lifetime generation for this slot.</summary>
    public uint Generation { get; }
    /// <summary>Whether this handle identifies a captured active lifetime.</summary>
    public bool IsValid => Generation != 0;
    public bool Equals(PluginEntityHandle other) => Kind == other.Kind && Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is PluginEntityHandle other && Equals(other);
    public override int GetHashCode() => ((int)Kind * 397) ^ Slot ^ unchecked((int)Generation);
    public static bool operator ==(PluginEntityHandle left, PluginEntityHandle right) => left.Equals(right);
    public static bool operator !=(PluginEntityHandle left, PluginEntityHandle right) => !left.Equals(right);
}

/// <summary>Immutable world-space collision snapshot. It never exposes a mutable Terraria entity.</summary>
public readonly struct PluginEntitySnapshot
{
    public PluginEntitySnapshot(PluginEntityKind kind, int id, float x, float y, float width, float height, bool friendly = false, bool hostile = false)
        : this(new PluginEntityHandle(kind, id, 0), x, y, width, height, friendly, hostile)
    {
    }

    /// <summary>Creates an immutable snapshot with generation-aware identity.</summary>
    public PluginEntitySnapshot(PluginEntityHandle handle, float x, float y, float width, float height, bool friendly = false, bool hostile = false)
    {
        Handle = handle;
        Kind = handle.Kind;
        Id = handle.Slot;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Friendly = friendly;
        Hostile = hostile;
    }

    public PluginEntityKind Kind { get; }
    /// <summary>Generation-aware identity for this captured lifetime.</summary>
    public PluginEntityHandle Handle { get; }
    public int Id { get; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public bool Friendly { get; }
    public bool Hostile { get; }
}

/// <summary>Copies current, read-only presentation snapshots into a caller-owned reusable buffer.</summary>
public interface IPluginEntitySnapshotService
{
    /// <summary>Number of active entity snapshots in the current shared captured frame.</summary>
    int ActiveEntityCount { get; }
    /// <summary>Appends active players, NPCs, and projectiles. Callers should reuse <paramref name="destination"/>.</summary>
    void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination);

    /// <summary>Appends recent host-captured melee collision rectangles. It is empty when unavailable.</summary>
    void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination);

    /// <summary>Looks up an active snapshot by its Terraria slot and category.</summary>
    bool TryGetBySlot(PluginEntityKind kind, int slot, out PluginEntitySnapshot entity);

    /// <summary>Looks up an active snapshot only when its generation still matches.</summary>
    bool TryGetByHandle(PluginEntityHandle handle, out PluginEntitySnapshot entity);

}

/// <summary>Optional high-frequency collision capture capability exposed only by integrations that support it.</summary>
public interface IPluginMeleeCollisionSnapshotService
{
    /// <summary>Requests version-sensitive melee collision snapshots while the owning scope remains active.</summary>
    IPluginRegistration RequestMeleeCollisionSnapshots();
}

/// <summary>Detached player presentation state captured at the integration update boundary.</summary>
public readonly struct PluginPlayerSnapshot
{
    /// <summary>Creates a player snapshot without host classification metadata for existing callers.</summary>
    public PluginPlayerSnapshot(int id, string name, int team, bool isActive, bool isDead, bool isGhost, int life, int lifeMax, int respawnTimer)
        : this(id, name, team, isActive, isDead, isGhost, life, lifeMax, respawnTimer, false)
    {
    }

    /// <summary>Creates a detached player snapshot with host-derived presentation metadata.</summary>
    public PluginPlayerSnapshot(int id, string name, int team, bool isActive, bool isDead, bool isGhost, int life, int lifeMax, int respawnTimer, bool isSuspectedBot)
        : this(new PluginEntityHandle(PluginEntityKind.Player, id, 0), name, team, isActive, isDead, isGhost, life, lifeMax, respawnTimer, isSuspectedBot)
    {
    }

    /// <summary>Creates a detached player snapshot with a generation-aware identity.</summary>
    public PluginPlayerSnapshot(PluginEntityHandle handle, string name, int team, bool isActive, bool isDead, bool isGhost, int life, int lifeMax, int respawnTimer, bool isSuspectedBot)
    {
        if (handle.Kind != PluginEntityKind.Player) throw new ArgumentException("Player snapshots require a player handle.", nameof(handle));
        Handle = handle; Id = handle.Slot; Name = name ?? string.Empty; Team = team; IsActive = isActive; IsDead = isDead; IsGhost = isGhost; Life = life; LifeMax = lifeMax; RespawnTimer = respawnTimer; IsSuspectedBot = isSuspectedBot;
    }
    public int Id { get; }
    /// <summary>Generation-aware player identity.</summary>
    public PluginEntityHandle Handle { get; }
    public string Name { get; }
    public int Team { get; }
    public bool IsActive { get; }
    public bool IsDead { get; }
    public bool IsGhost { get; }
    public int Life { get; }
    public int LifeMax { get; }
    public int RespawnTimer { get; }
    /// <summary>Gets whether the host's bounded heuristic classified this account as a suspected bot.</summary>
    public bool IsSuspectedBot { get; }
}

/// <summary>One timed buff from a detached player snapshot.</summary>
public readonly struct PluginBuffSnapshot
{
    public PluginBuffSnapshot(int id, int timeLeft) { Id = id; TimeLeft = timeLeft; }
    public int Id { get; }
    public int TimeLeft { get; }
}

/// <summary>Read-only player lookup backed by the shared host snapshot cache.</summary>
public interface IPluginPlayerService
{
    /// <summary>Number of active or ghost player snapshots in the current shared captured frame.</summary>
    int ActivePlayerCount { get; }
    /// <summary>Gets a detached player snapshot by Terraria slot, returning false when no player is present.</summary>
    bool TryGet(int playerId, out PluginPlayerSnapshot player);
    /// <summary>Gets a player only when its captured generation still matches.</summary>
    bool TryGet(PluginEntityHandle handle, out PluginPlayerSnapshot player);
    /// <summary>Gets a player name by slot without exposing a live Terraria player object.</summary>
    string? GetName(int playerId);
    /// <summary>Appends active and ghost player snapshots to a caller-owned reusable collection.</summary>
    void CopyPlayers(ICollection<PluginPlayerSnapshot> destination);
    /// <summary>Appends timed buffs for one captured player to a caller-owned reusable collection.</summary>
    void CopyBuffs(int playerId, ICollection<PluginBuffSnapshot> destination);
}

/// <summary>Optional demand controls for capture categories that are expensive to populate.</summary>
public interface IPluginPlayerSnapshotDemandService
{
    /// <summary>Requests detached timed buff snapshots while the returned registration remains active.</summary>
    IPluginRegistration RequestPlayerBuffSnapshots();
    /// <summary>Requests bounded host-side suspected-bot classification while the registration remains active.</summary>
    IPluginRegistration RequestSuspectedBotClassification();
    /// <summary>Requests one immediate refresh of the bounded suspected-bot classification cache.</summary>
    void RefreshSuspectedBotClassification();
    /// <summary>Monotonically increases after a requested suspected-bot classification snapshot is published.</summary>
    long SuspectedBotClassificationVersion { get; }
}

#pragma warning restore CS1591
