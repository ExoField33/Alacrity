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

/// <summary>Immutable world-space collision snapshot. It never exposes a mutable Terraria entity.</summary>
public readonly struct PluginEntitySnapshot
{
    public PluginEntitySnapshot(PluginEntityKind kind, int id, float x, float y, float width, float height, bool friendly = false, bool hostile = false)
    {
        Kind = kind;
        Id = id;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Friendly = friendly;
        Hostile = hostile;
    }

    public PluginEntityKind Kind { get; }
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
    /// <summary>Appends active players, NPCs, and projectiles. Callers should reuse <paramref name="destination"/>.</summary>
    void CopyActiveEntities(ICollection<PluginEntitySnapshot> destination);

    /// <summary>Appends recent host-captured melee collision rectangles. It is empty when unavailable.</summary>
    void CopyMeleeHitboxes(ICollection<PluginEntitySnapshot> destination);
}

#pragma warning restore CS1591
