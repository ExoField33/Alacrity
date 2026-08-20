using System;
using System.Collections.Generic;

namespace AlacrityTerraria.Rendering.Clothing;

/// <summary>
/// Bounds first-use clothing visual work without retaining Terraria entities, items, or graphics
/// resources. It is render-thread owned: the caller supplies monotonic timestamps and marks a
/// configuration ready only after Terraria completed its native draw path.
/// </summary>
internal sealed class ClothingEntityPreparationGate
{
    private readonly HashSet<ulong> readyConfigurations;
    private readonly HashSet<ulong> admittedConfigurations;
    private readonly long budgetTicks;
    private int worldIdentity = int.MinValue;
    private long deadline;

    internal ClothingEntityPreparationGate(long budgetTicks, int maximumReadyConfigurations)
    {
        if (budgetTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetTicks));
        }

        if (maximumReadyConfigurations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReadyConfigurations));
        }

        this.budgetTicks = budgetTicks;
        readyConfigurations = new HashSet<ulong>(maximumReadyConfigurations);
        admittedConfigurations = new HashSet<ulong>(maximumReadyConfigurations);
        MaximumReadyConfigurations = maximumReadyConfigurations;
    }

    internal int MaximumReadyConfigurations { get; }

    internal int ReadyCount => readyConfigurations.Count;

    internal int AdmittedCount => admittedConfigurations.Count;

    internal void BeginFrame(int currentWorldIdentity, long timestamp)
    {
        if (worldIdentity != currentWorldIdentity)
        {
            Reset();
            worldIdentity = currentWorldIdentity;
        }

        admittedConfigurations.Clear();
        deadline = AddSaturating(timestamp, budgetTicks);
    }

    /// <summary>
    /// Returns true for an already-ready visual immediately. New configurations are admitted at
    /// most once per frame and only while the current frame retains cold-work budget.
    /// </summary>
    internal bool TryAdmit(int entityKind, long visualConfiguration, long timestamp)
    {
        ulong key = ComposeKey(entityKind, visualConfiguration);
        if (readyConfigurations.Contains(key))
        {
            return true;
        }

        if (admittedConfigurations.Contains(key) || timestamp >= deadline)
        {
            return false;
        }

        admittedConfigurations.Add(key);
        return true;
    }

    internal void Complete(int entityKind, long visualConfiguration)
    {
        ulong key = ComposeKey(entityKind, visualConfiguration);
        if (!admittedConfigurations.Remove(key))
        {
            return;
        }

        if (readyConfigurations.Count == MaximumReadyConfigurations)
        {
            // The cache contains only reusable configuration keys, never native references. A
            // bounded clear is preferable to keeping unbounded world-lifetime state.
            readyConfigurations.Clear();
        }

        readyConfigurations.Add(key);
    }

    internal void Reset()
    {
        readyConfigurations.Clear();
        admittedConfigurations.Clear();
        deadline = 0;
    }

    private static ulong ComposeKey(int entityKind, long visualConfiguration)
    {
        unchecked
        {
            return ((ulong)(uint)entityKind << 56) ^ (ulong)visualConfiguration;
        }
    }

    private static long AddSaturating(long timestamp, long duration)
    {
        if (duration > 0 && timestamp > long.MaxValue - duration)
        {
            return long.MaxValue;
        }

        return timestamp + duration;
    }
}
