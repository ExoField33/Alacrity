namespace TileStorageTransformFixture;

public sealed class Tile
{
    public ushort type;
    public short frameX;

    public Tile()
    {
    }

    public Tile(Tile source)
    {
        type = source.type;
        frameX = source.frameX;
    }

    public bool active()
    {
        return type != 0;
    }
}

public struct CompactTileData
{
    public ushort Type;
    public short FrameX;

    public CompactTileData(ushort type, short frameX)
    {
        Type = type;
        FrameX = frameX;
    }
}

public readonly struct CompactTileReference
{
    public CompactTileReference(CompactTileReferenceState state, int index, int version)
    {
        State = state;
        Index = index;
        Version = version;
    }

    public CompactTileReferenceState? State { get; }
    public int Index { get; }
    public int Version { get; }
}

public sealed class CompactTileReferenceState
{
    private readonly CompactTileData[] data;
    private readonly bool[] materialized;
    private readonly int[] versions;
    private readonly Dictionary<long, CompactTileStandalone> displaced = new();

    public CompactTileReferenceState(CompactTileData[] data, bool[] materialized)
    {
        this.data = data;
        this.materialized = materialized;
        versions = new int[data.Length];
    }

    public CompactTileReference Get(int index)
    {
        return materialized[index] ? new CompactTileReference(this, index, versions[index]) : default;
    }

    public ref CompactTileData GetData(int index, int version)
    {
        if (versions[index] == version && materialized[index])
            return ref data[index];
        if (displaced.TryGetValue(Key(index, version), out CompactTileStandalone? value))
            return ref value.Data;
        throw new NullReferenceException("Tile reference is null.");
    }

    public void Replace(int index)
    {
        if (materialized[index])
            displaced.TryAdd(Key(index, versions[index]), new CompactTileStandalone { Data = data[index] });
        versions[index]++;
        materialized[index] = false;
        data[index] = default;
    }

    private static long Key(int index, int version) => ((long)index << 32) | (uint)version;
}

public sealed class CompactTileStandalone
{
    public CompactTileData Data;
}

public static class Framing
{
    public static Tile GetTileSafely(int x, int y) => Main.tile[x, y];
}

public static class Player
{
    public static Tile GetFloorTile(int x, int y) => Main.tile[x, y];
}

public static class PlayerSittingHelper
{
    public static bool TryGetSittingBlock(int x, int y, out Tile tile)
    {
        tile = Main.tile[x, y];
        return tile != null;
    }
}

public static class WorldGen
{
    public static void Convert_ActuallyConvertTile(ref Tile tile, ushort type) => tile.type = type;
}

public sealed class TileDrawInfo { public Tile tileCache = null!; }
public sealed class DartTrapPlacementAttempt { public Tile t = null!; }
public sealed class BallCollisionEvent { public Tile Tile = null!; }
public sealed class BallPassThroughEvent { public Tile Tile = null!; }

public static class Main
{
    public static Tile[,] tile = null!;
    public static int width;
    public static int height;
    public static Tile storedTile = null!;
    public static TileDrawInfo drawInfo = new();
    public static DartTrapPlacementAttempt dartTrap = new();
    public static BallCollisionEvent ballCollision = new();
    public static BallPassThroughEvent ballPassThrough = new();

    public static void Initialize(int worldWidth, int height)
    {
        width = worldWidth;
        Main.height = height;
        tile = new Tile[worldWidth, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < worldWidth; x++)
                tile[x, y] = new Tile();
        }
    }

    public static ushort ReadType(int x, int y)
    {
        return tile[x, y].type;
    }

    public static void WriteType(int x, int y, ushort value)
    {
        tile[x, y].type = value;
    }

    public static short ReadFrameX(int x, int y)
    {
        return tile[x, y].frameX;
    }

    public static void WriteFrameX(int x, int y, short value)
    {
        tile[x, y].frameX = value;
    }

    public static bool IsMissing(int x, int y)
    {
        return tile[x, y] == null;
    }

    public static void Clear(int x, int y)
    {
        tile[x, y] = null!;
    }

    public static void EnsureAndWriteType(int x, int y, ushort value)
    {
        if (tile[x, y] == null)
            tile[x, y] = new Tile();
        tile[x, y].type = value;
    }

    public static void CopyCell(int sourceX, int sourceY, int destinationX, int destinationY)
    {
        tile[destinationX, destinationY] = new Tile(tile[sourceX, sourceY]);
    }

    public static bool ReadActive(int x, int y)
    {
        return tile[x, y].active();
    }

    public static int GetWidth()
    {
        return tile.GetLength(0);
    }

    public static int GetHeight()
    {
        return tile.GetLength(1);
    }

    public static Tile GetCell(int x, int y)
    {
        return tile[x, y];
    }

    public static ushort ReadTypeThroughFraming(int x, int y) => Framing.GetTileSafely(x, y).type;
    public static ushort ReadTypeThroughFloorTile(int x, int y) => Player.GetFloorTile(x, y).type;
    public static bool TryGetSittingBlock(int x, int y, out Tile tile) => PlayerSittingHelper.TryGetSittingBlock(x, y, out tile);
    public static void ConvertTile(int x, int y, ushort type) => WorldGen.Convert_ActuallyConvertTile(ref tile[x, y], type);

    public static void StoreReferenceFields(int x, int y)
    {
        Tile value = tile[x, y];
        drawInfo.tileCache = value;
        dartTrap.t = value;
        ballCollision.Tile = value;
        ballPassThrough.Tile = value;
    }

    public static ushort ReadReferenceFields()
    {
        return (ushort)(drawInfo.tileCache.type + dartTrap.t.type + ballCollision.Tile.type + ballPassThrough.Tile.type);
    }

    public static ref Tile GetCellAddress(int x, int y)
    {
        return ref tile[x, y];
    }

    public static ushort ReadTypeThroughAddress(int x, int y)
    {
        return GetCellAddress(x, y).type;
    }

    public static void WriteTypeThroughAddress(int x, int y, ushort type)
    {
        GetCellAddress(x, y).type = type;
    }

    public static void CopyTypeViaAddresses(int sourceX, int sourceY, int destinationX, int destinationY)
    {
        CopyTypeByReference(ref GetCellAddress(destinationX, destinationY), ref GetCellAddress(sourceX, sourceY));
    }

    public static ushort ReadTypeThroughLocal(int x, int y)
    {
        Tile local = tile[x, y];
        return local.type;
    }

    public static void WriteTypeThroughParameter(Tile value, ushort type)
    {
        value.type = type;
    }

    public static void WriteTypeViaParameter(int x, int y, ushort type)
    {
        WriteTypeThroughParameter(tile[x, y], type);
    }

    public static void WriteTypeThroughReturnedCell(int x, int y, ushort type)
    {
        GetCell(x, y).type = type;
    }

    public static bool ReturnedCellIsMissing(int x, int y)
    {
        return GetCell(x, y) == null;
    }

    public static void StoreCell(int x, int y) => storedTile = tile[x, y];
    public static ushort ReadStoredType() => storedTile.type;
    public static void WriteStoredType(ushort value) => storedTile.type = value;
    public static bool IsStoredMissing() => storedTile == null;
    public static void ClearStored() => storedTile = null!;

    public static ushort ReadTypeByReference(ref Tile value) => value.type;
    public static ushort ReadTypeViaByReference(int x, int y) => ReadTypeByReference(ref tile[x, y]);

    public static void CopyTypeByReference(ref Tile destination, ref Tile source) => destination.type = source.type;
    public static void CopyTypeViaByReference(int sourceX, int sourceY, int destinationX, int destinationY) => CopyTypeByReference(ref tile[destinationX, destinationY], ref tile[sourceX, sourceY]);
}

public static class TileRuntime
{
    public static int FieldCallCount;

    public static CompactTileReferenceState CreateReferenceState(CompactTileData[] data, bool[] materialized) => new(data, materialized);

    public static void ClearCompactCell(CompactTileReferenceState state, int index) => state.Replace(index);

    public static void CopyCompactCell(CompactTileData[] data, bool[] materialized, CompactTileReferenceState state, int width, int sourceX, int sourceY, int destinationX, int destinationY)
    {
        int source = sourceX + sourceY * width;
        int destination = destinationX + destinationY * width;
        RequireMaterialized(materialized, source);
        state.Replace(destination);
        data[destination] = data[source];
        materialized[destination] = true;
    }

    public static void FillMaterialized(bool[] materialized)
    {
        Array.Fill(materialized, true);
    }

    public static void RequireMaterialized(bool[] materialized, int index)
    {
        if (!materialized[index])
            throw new NullReferenceException("Tile reference is null.");
    }

    public static CompactTileReference GetCompactReference(CompactTileReferenceState state, int width, int x, int y)
    {
        int index = x + y * width;
        return state.Get(index);
    }

    public static bool IsNull(CompactTileReference tile)
    {
        return tile.State == null;
    }

    public static ushort GetCompactTypeValue(CompactTileReference tile)
    {
        return RequireCompactReference(tile).Type;
    }

    public static void SetCompactTypeValue(CompactTileReference tile, ushort value)
    {
        RequireCompactReference(tile).Type = value;
    }

    private static ref CompactTileData RequireCompactReference(CompactTileReference tile)
    {
        if (tile.State == null)
            throw new NullReferenceException("Tile reference is null.");
        return ref tile.State.GetData(tile.Index, tile.Version);
    }

    public static ushort GetTypeValue(Tile tile)
    {
        FieldCallCount++;
        return tile.type;
    }

    public static void SetTypeValue(Tile tile, ushort value)
    {
        FieldCallCount++;
        tile.type = value;
    }

    public static short GetFrameX(Tile tile)
    {
        FieldCallCount++;
        return tile.frameX;
    }

    public static void SetFrameX(Tile tile, short value)
    {
        FieldCallCount++;
        tile.frameX = value;
    }
}
