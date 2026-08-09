using System;
using System.Collections.Generic;
using System.Threading;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Terraria;

namespace AlacrityTerraria.GameState.World;

/// <summary>
/// Captures only the currently visible tile-section window on Terraria's update thread. Published
/// snapshots are detached and copied under a read lock, allowing plugin draw or worker callbacks
/// to consume them without touching <see cref="Main"/>.
/// </summary>
internal sealed class TerrariaWorldSectionSnapshotCache
{
    private const int TilesPerSectionX = 200;
    private const int TilesPerSectionY = 150;
    private const int PixelsPerTile = 16;

    private readonly ReaderWriterLockSlim gate = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    private readonly List<TerrariaWorldSectionService> consumers = new List<TerrariaWorldSectionService>();
    private Snapshot current = Snapshot.Empty;
    private Snapshot alternate = Snapshot.Empty;
    private uint capturedTick = uint.MaxValue;
    private int capturedFrameCount;

    /// <summary>Captures at most once per simulation tick and returns immediately without consumers.</summary>
    internal void CaptureForCurrentTick()
    {
        gate.EnterUpgradeableReadLock();
        try
        {
            // Reading Main is avoided entirely until at least one activation has consumed this
            // capability. Installed-but-idle diagnostics therefore add no recurring section work.
            if (consumers.Count == 0)
            {
                return;
            }

            uint tick = Main.GameUpdateCount;
            if (tick == capturedTick)
            {
                return;
            }

            int margin = GetMaximumRequestedMargin();
            gate.EnterWriteLock();
            try
            {
                if (consumers.Count == 0 || tick == capturedTick)
                {
                    return;
                }

                Capture(ref alternate, margin, tick);
                Snapshot prior = current;
                current = alternate;
                alternate = prior;
                capturedTick = tick;
                Interlocked.Increment(ref capturedFrameCount);
            }
            finally
            {
                gate.ExitWriteLock();
            }
        }
        finally
        {
            gate.ExitUpgradeableReadLock();
        }
    }

    internal void RegisterDemand(TerrariaWorldSectionService service)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        gate.EnterWriteLock();
        try
        {
            if (!consumers.Contains(service))
            {
                consumers.Add(service);
                capturedTick = uint.MaxValue;
            }
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    internal void UnregisterDemand(TerrariaWorldSectionService service)
    {
        gate.EnterWriteLock();
        try
        {
            consumers.Remove(service);
            if (consumers.Count == 0)
            {
                current = Snapshot.Empty;
                alternate = Snapshot.Empty;
                capturedTick = uint.MaxValue;
            }
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    internal void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int requestedMargin)
    {
        gate.EnterReadLock();
        try
        {
            Snapshot snapshot = current;
            int startX = Math.Max(0, snapshot.BaseStartX - requestedMargin);
            int startY = Math.Max(0, snapshot.BaseStartY - requestedMargin);
            int endX = Math.Min(snapshot.MaximumSectionX, snapshot.BaseEndX + requestedMargin);
            int endY = Math.Min(snapshot.MaximumSectionY, snapshot.BaseEndY + requestedMargin);
            for (int index = 0; index < snapshot.Count; index++)
            {
                PluginWorldSectionSnapshot section = snapshot.Sections[index];
                if (section.SectionX >= startX && section.SectionX <= endX &&
                    section.SectionY >= startY && section.SectionY <= endY)
                {
                    destination.Add(section);
                }
            }
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    internal int ConsumerCount
    {
        get
        {
            gate.EnterReadLock();
            try
            {
                return consumers.Count;
            }
            finally
            {
                gate.ExitReadLock();
            }
        }
    }

    internal int CapturedFrameCount => Volatile.Read(ref capturedFrameCount);

    private int GetMaximumRequestedMargin()
    {
        int maximum = 0;
        for (int index = 0; index < consumers.Count; index++)
        {
            maximum = Math.Max(maximum, consumers[index].RequestedMargin);
        }

        return maximum;
    }

    private static void Capture(ref Snapshot snapshot, int margin, uint tick)
    {
        if (Main.gameMenu || Main.sectionManager == null || Main.maxSectionsX <= 0 || Main.maxSectionsY <= 0)
        {
            snapshot.SetEmpty(tick);
            return;
        }

        int sectionWidthPixels = TilesPerSectionX * PixelsPerTile;
        int sectionHeightPixels = TilesPerSectionY * PixelsPerTile;
        float zoom = Main.GameViewMatrix == null ? 1f : Main.GameViewMatrix.Zoom.X;
        Vector2 screen = Main.screenPosition;
        TerrariaWorldSectionBounds bounds = TerrariaWorldSectionBounds.Calculate(
            screen.X,
            screen.Y,
            Main.screenWidth,
            Main.screenHeight,
            zoom,
            Main.maxSectionsX - 1,
            Main.maxSectionsY - 1,
            margin,
            sectionWidthPixels,
            sectionHeightPixels);
        int baseStartX = bounds.BaseStartX;
        int baseStartY = bounds.BaseStartY;
        int baseEndX = bounds.BaseEndX;
        int baseEndY = bounds.BaseEndY;
        int startX = bounds.StartX;
        int startY = bounds.StartY;
        int endX = bounds.EndX;
        int endY = bounds.EndY;
        int required = Math.Max(0, endX - startX + 1) * Math.Max(0, endY - startY + 1);
        snapshot.EnsureCapacity(required);

        int count = 0;
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                snapshot.Sections[count++] = new PluginWorldSectionSnapshot(
                    x,
                    y,
                    x * sectionWidthPixels,
                    y * sectionHeightPixels,
                    sectionWidthPixels,
                    sectionHeightPixels,
                    Main.sectionManager.SectionLoaded(x, y));
            }
        }

        snapshot.SetBounds(baseStartX, baseStartY, baseEndX, baseEndY, Main.maxSectionsX - 1, Main.maxSectionsY - 1, count, tick);
    }

    private sealed class Snapshot
    {
        internal static readonly Snapshot Empty = new Snapshot(Array.Empty<PluginWorldSectionSnapshot>());

        internal Snapshot(PluginWorldSectionSnapshot[] sections)
        {
            Sections = sections;
        }

        internal PluginWorldSectionSnapshot[] Sections;
        internal int BaseStartX;
        internal int BaseStartY;
        internal int BaseEndX;
        internal int BaseEndY;
        internal int MaximumSectionX;
        internal int MaximumSectionY;
        internal int Count;
        internal uint Tick;

        internal void EnsureCapacity(int required)
        {
            if (Sections.Length < required)
            {
                Array.Resize(ref Sections, required);
            }
        }

        internal void SetBounds(int baseStartX, int baseStartY, int baseEndX, int baseEndY, int maximumSectionX, int maximumSectionY, int count, uint tick)
        {
            BaseStartX = baseStartX;
            BaseStartY = baseStartY;
            BaseEndX = baseEndX;
            BaseEndY = baseEndY;
            MaximumSectionX = maximumSectionX;
            MaximumSectionY = maximumSectionY;
            Count = count;
            Tick = tick;
        }

        internal void SetEmpty(uint tick)
        {
            SetBounds(0, 0, -1, -1, -1, -1, 0, tick);
        }
    }
}
