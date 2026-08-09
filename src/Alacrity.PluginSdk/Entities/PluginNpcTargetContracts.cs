using System;
using System.Collections.Generic;

#pragma warning disable CS1591

namespace Alacrity.PluginSdk;

/// <summary>
/// Immutable relationship between a hostile NPC and the player it currently targets. Values are
/// captured by the host on the update thread and are safe to consume from presentation callbacks
/// or worker threads until the caller discards the value.
/// </summary>
public readonly struct PluginNpcTargetSnapshot
{
    public PluginNpcTargetSnapshot(PluginEntityHandle npc, PluginEntityHandle target, float npcCenterX, float npcCenterY, float targetCenterX, float targetCenterY, bool isBoss)
    {
        if (npc.Kind != PluginEntityKind.Npc) throw new ArgumentException("NPC targeting snapshots require an NPC handle.", nameof(npc));
        if (target.Kind != PluginEntityKind.Player) throw new ArgumentException("NPC targeting snapshots require a player handle.", nameof(target));
        Npc = npc;
        Target = target;
        NpcCenterX = npcCenterX;
        NpcCenterY = npcCenterY;
        TargetCenterX = targetCenterX;
        TargetCenterY = targetCenterY;
        IsBoss = isBoss;
    }

    public PluginEntityHandle Npc { get; }
    public PluginEntityHandle Target { get; }
    public float NpcCenterX { get; }
    public float NpcCenterY { get; }
    public float TargetCenterX { get; }
    public float TargetCenterY { get; }
    public bool IsBoss { get; }
}

/// <summary>
/// Read-only hostile NPC targeting snapshots backed by a shared, demand-gated host cache. The
/// service is activation-scoped; calls after disable throw rather than retaining access to a later
/// activation's data.
/// </summary>
public interface IPluginNpcTargetSnapshotService
{
    /// <summary>
    /// Appends captured targeting relationships into a caller-owned reusable buffer. It does not
    /// clear the collection and never exposes a live NPC or player object.
    /// </summary>
    void CopyHostileNpcTargets(ICollection<PluginNpcTargetSnapshot> destination);
}

#pragma warning restore CS1591
