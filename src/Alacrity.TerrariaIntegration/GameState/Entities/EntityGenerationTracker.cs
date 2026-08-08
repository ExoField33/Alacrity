using System;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.GameState.Entities;

/// <summary>Allocation-stable generation tracker for volatile Terraria entity slots.</summary>
internal sealed class EntityGenerationTracker
{
    private uint[] playerGenerations = Array.Empty<uint>();
    private uint[] npcGenerations = Array.Empty<uint>();
    private uint[] projectileGenerations = Array.Empty<uint>();
    private bool[] playerActive = Array.Empty<bool>();
    private bool[] npcActive = Array.Empty<bool>();
    private bool[] projectileActive = Array.Empty<bool>();

    internal void EnsureCapacity(int players, int npcs, int projectiles)
    {
        Resize(ref playerGenerations, ref playerActive, players);
        Resize(ref npcGenerations, ref npcActive, npcs);
        Resize(ref projectileGenerations, ref projectileActive, projectiles);
    }

    internal PluginEntityHandle GetHandle(PluginEntityKind kind, int slot, bool active)
    {
        if (slot < 0) return default;
        Select(kind, out uint[] generations, out bool[] activeSlots);
        if (slot >= generations.Length) return default;
        if (!active) { activeSlots[slot] = false; return default; }
        if (!activeSlots[slot])
        {
            uint next = unchecked(generations[slot] + 1);
            generations[slot] = next == 0 ? 1 : next;
            activeSlots[slot] = true;
        }
        return new PluginEntityHandle(kind, slot, generations[slot]);
    }

    private static void Resize(ref uint[] generations, ref bool[] active, int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (generations.Length < capacity) Array.Resize(ref generations, capacity);
        if (active.Length < capacity) Array.Resize(ref active, capacity);
    }

    private void Select(PluginEntityKind kind, out uint[] generations, out bool[] active)
    {
        switch (kind)
        {
            case PluginEntityKind.Player: generations = playerGenerations; active = playerActive; return;
            case PluginEntityKind.Npc: generations = npcGenerations; active = npcActive; return;
            case PluginEntityKind.Projectile: generations = projectileGenerations; active = projectileActive; return;
            default: throw new ArgumentOutOfRangeException(nameof(kind), "Only slot-backed Terraria entity kinds have generations.");
        }
    }
}
