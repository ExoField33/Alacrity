using System.Diagnostics;
using System.Text.Json;
using AlacrityTerraria;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            BenchmarkOptions options = ParseArguments(args);
            BenchmarkReport report = Run(options);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Tile storage benchmark failed: {exception.Message}");
            return 1;
        }
    }

    private static BenchmarkOptions ParseArguments(string[] args)
    {
        if (args.Length == 0)
            return new BenchmarkOptions(640, 360, 250_000, 2, 7);
        if (args.Length is not (3 or 5) || !int.TryParse(args[0], out int width) || !int.TryParse(args[1], out int height) || !int.TryParse(args[2], out int randomAccesses))
            throw new ArgumentException("Usage: TileStorageBenchmarks [width height randomAccesses [warmups samples]]");
        int warmups = 2;
        int samples = 7;
        if (args.Length == 5 && (!int.TryParse(args[3], out warmups) || !int.TryParse(args[4], out samples)))
            throw new ArgumentException("Warmups and samples must be integers.");
        if (width <= 0 || height <= 0 || randomAccesses <= 0 || warmups < 0 || samples <= 0 || (samples & 1) == 0)
            throw new ArgumentOutOfRangeException(nameof(args));
        return new BenchmarkOptions(width, height, randomAccesses, warmups, samples);
    }

    private static BenchmarkReport Run(BenchmarkOptions options)
    {
        return new BenchmarkReport
        {
            Scope = "Synthetic allocation/access model only; this does not measure live Terraria, world IO, or network compatibility. Timings are warm-up-discarded sample medians.",
            Width = options.Width,
            Height = options.Height,
            RandomAccesses = options.RandomAccesses,
            Warmups = options.Warmups,
            Samples = options.Samples,
            LegacyObjectGrid = RunRepeated(() => RunLegacy(options.Width, options.Height, options.RandomAccesses), options.Warmups, options.Samples),
            FlatTileMap = RunRepeated(() => RunFlat(options.Width, options.Height, options.RandomAccesses), options.Warmups, options.Samples)
        };
    }

    private static BenchmarkResult RunRepeated(Func<BenchmarkResult> run, int warmups, int samples)
    {
        for (int index = 0; index < warmups; index++)
            _ = run();

        var results = new BenchmarkResult[samples];
        for (int index = 0; index < results.Length; index++)
            results[index] = run();
        int checksum = results[0].Checksum;
        if (results.Any(result => result.Checksum != checksum))
            throw new InvalidOperationException("A synthetic benchmark produced inconsistent tile-access checksums.");

        int middle = results.Length / 2;
        return new BenchmarkResult
        {
            AllocationMilliseconds = results.Select(result => result.AllocationMilliseconds).OrderBy(value => value).ElementAt(middle),
            SequentialMilliseconds = results.Select(result => result.SequentialMilliseconds).OrderBy(value => value).ElementAt(middle),
            RandomMilliseconds = results.Select(result => result.RandomMilliseconds).OrderBy(value => value).ElementAt(middle),
            ThreadAllocatedBytes = results.Select(result => result.ThreadAllocatedBytes).OrderBy(value => value).ElementAt(middle),
            ManagedHeapDeltaBytes = results.Select(result => result.ManagedHeapDeltaBytes).OrderBy(value => value).ElementAt(middle),
            Checksum = checksum
        };
    }

    private static BenchmarkResult RunLegacy(int width, int height, int randomAccesses)
    {
        ForceCollection();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long memoryBefore = GC.GetTotalMemory(true);
        var timer = Stopwatch.StartNew();
        var tiles = new LegacyTile[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tiles[x, y] = new LegacyTile
                {
                    Type = (ushort)((x + y) & ushort.MaxValue),
                    Wall = (ushort)((x * 3 + y) & ushort.MaxValue),
                    TileHeader = (ushort)((x ^ y) & ushort.MaxValue),
                    FrameX = (short)x,
                    FrameY = (short)y,
                    Liquid = (byte)(x + y),
                    Header = (byte)x,
                    Header2 = (byte)y,
                    Header3 = (byte)(x ^ y)
                };
            }
        }
        timer.Stop();
        long allocationMilliseconds = timer.ElapsedMilliseconds;
        long memoryAfter = GC.GetTotalMemory(false);

        timer.Restart();
        int checksum = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                checksum += tiles[x, y].Type;
        }
        timer.Stop();
        long sequentialMilliseconds = timer.ElapsedMilliseconds;

        timer.Restart();
        uint state = 0xC0FFEEu;
        for (int index = 0; index < randomAccesses; index++)
        {
            state = Next(state);
            int x = (int)(state % (uint)width);
            state = Next(state);
            int y = (int)(state % (uint)height);
            checksum += tiles[x, y].Wall;
        }
        timer.Stop();
        long randomMilliseconds = timer.ElapsedMilliseconds;

        return new BenchmarkResult
        {
            AllocationMilliseconds = allocationMilliseconds,
            SequentialMilliseconds = sequentialMilliseconds,
            RandomMilliseconds = randomMilliseconds,
            ThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            ManagedHeapDeltaBytes = memoryAfter - memoryBefore,
            Checksum = checksum
        };
    }

    private static BenchmarkResult RunFlat(int width, int height, int randomAccesses)
    {
        ForceCollection();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long memoryBefore = GC.GetTotalMemory(true);
        var timer = Stopwatch.StartNew();
        var tiles = new AlacrityTileMap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ref TileData tile = ref tiles.EnsureMaterialized(x, y);
                tile.Type = (ushort)((x + y) & ushort.MaxValue);
                tile.Wall = (ushort)((x * 3 + y) & ushort.MaxValue);
                tile.TileHeader = (ushort)((x ^ y) & ushort.MaxValue);
                tile.FrameX = (short)x;
                tile.FrameY = (short)y;
                tile.Liquid = (byte)(x + y);
                tile.Header = (byte)x;
                tile.Header2 = (byte)y;
                tile.Header3 = (byte)(x ^ y);
            }
        }
        timer.Stop();
        long allocationMilliseconds = timer.ElapsedMilliseconds;
        long memoryAfter = GC.GetTotalMemory(false);

        timer.Restart();
        int checksum = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                checksum += tiles.GetDataUnchecked(x, y).Type;
        }
        timer.Stop();
        long sequentialMilliseconds = timer.ElapsedMilliseconds;

        timer.Restart();
        uint state = 0xC0FFEEu;
        for (int index = 0; index < randomAccesses; index++)
        {
            state = Next(state);
            int x = (int)(state % (uint)width);
            state = Next(state);
            int y = (int)(state % (uint)height);
            checksum += tiles.GetDataUnchecked(x, y).Wall;
        }
        timer.Stop();
        long randomMilliseconds = timer.ElapsedMilliseconds;

        return new BenchmarkResult
        {
            AllocationMilliseconds = allocationMilliseconds,
            SequentialMilliseconds = sequentialMilliseconds,
            RandomMilliseconds = randomMilliseconds,
            ThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            ManagedHeapDeltaBytes = memoryAfter - memoryBefore,
            Checksum = checksum
        };
    }

    private static uint Next(uint value)
    {
        return value * 1664525u + 1013904223u;
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

internal sealed class LegacyTile
{
    public ushort Type;
    public ushort Wall;
    public ushort TileHeader;
    public short FrameX;
    public short FrameY;
    public byte Liquid;
    public byte Header;
    public byte Header2;
    public byte Header3;
}

internal sealed class BenchmarkReport
{
    public string Scope { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int RandomAccesses { get; init; }
    public int Warmups { get; init; }
    public int Samples { get; init; }
    public BenchmarkResult LegacyObjectGrid { get; init; } = new();
    public BenchmarkResult FlatTileMap { get; init; } = new();
}

internal sealed class BenchmarkResult
{
    public long AllocationMilliseconds { get; init; }
    public long SequentialMilliseconds { get; init; }
    public long RandomMilliseconds { get; init; }
    public long ThreadAllocatedBytes { get; init; }
    public long ManagedHeapDeltaBytes { get; init; }
    public int Checksum { get; init; }
}

internal readonly record struct BenchmarkOptions(int Width, int Height, int RandomAccesses, int Warmups, int Samples);
