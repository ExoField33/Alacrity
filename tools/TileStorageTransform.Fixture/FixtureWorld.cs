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

public static class Main
{
    public static Tile[,] tile = null!;
    public static int width;

    public static void Initialize(int worldWidth, int height)
    {
        width = worldWidth;
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
}

public static class TileRuntime
{
    public static int FieldCallCount;

    public static void CopyCompactCell(CompactTileData[] data, bool[] materialized, int width, int sourceX, int sourceY, int destinationX, int destinationY)
    {
        int source = sourceX + sourceY * width;
        int destination = destinationX + destinationY * width;
        data[destination] = data[source];
        materialized[destination] = true;
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
