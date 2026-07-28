using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using AlacrityTerraria;
using Terraria;
using Terraria.DataStructures;

internal static class Program
{
    private static readonly MethodInfo ClearMetadataMethod = typeof(Tile).GetMethod("ClearMetadata", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Tile).FullName, "ClearMetadata");

    private static int Main()
    {
        try
        {
            VerifyRawFieldRoundTrip();
            VerifyMapSnapshotRoundTrip();
            VerifyTestFixtureSnapshotRoundTrip();
            VerifyVanillaClearAndCopySemantics();
            VerifyHeaderAccessors();
            Console.WriteLine("Tile storage compatibility tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyRawFieldRoundTrip()
    {
        Assert(Marshal.SizeOf<TileData>() == 14, "TileData layout must remain fourteen bytes.");
        var source = new Tile
        {
            type = ushort.MaxValue,
            wall = 431,
            sTileHeader = 0xFFFF,
            frameX = short.MinValue,
            frameY = short.MaxValue,
            liquid = byte.MaxValue,
            bTileHeader = 0xE1,
            bTileHeader2 = 0x7F,
            bTileHeader3 = 0xD4
        };

        TileData data = TileDataCompatibility.Capture(source);
        var restored = new Tile();
        TileDataCompatibility.Apply(data, restored);
        AssertSameRawState(source, restored);
    }

    private static void VerifyMapSnapshotRoundTrip()
    {
        var source = new Tile
        {
            type = 321,
            wall = 42,
            sTileHeader = 0x8A5A,
            frameX = -18,
            frameY = 36,
            liquid = 255,
            bTileHeader = 3,
            bTileHeader2 = 4,
            bTileHeader3 = 5
        };
        var map = new AlacrityTileMap(2, 2);
        map.SetSnapshot(1, 1, new TileSnapshot(TileDataCompatibility.Capture(source), true));
        TileSnapshot snapshot = map.GetSnapshot(1, 1);
        Assert(snapshot.IsMaterialized, "A captured tile snapshot must preserve materialization.");

        var restored = new Tile();
        TileDataCompatibility.Apply(snapshot.Data, restored);
        AssertSameRawState(source, restored);
    }

    // This is a test fixture only; it is not Terraria world or network serialization.
    private static void VerifyTestFixtureSnapshotRoundTrip()
    {
        var source = new Tile
        {
            type = ushort.MaxValue,
            wall = 431,
            sTileHeader = 0x8A5A,
            frameX = short.MinValue,
            frameY = short.MaxValue,
            liquid = byte.MaxValue,
            bTileHeader = 0xE1,
            bTileHeader2 = 0x7F,
            bTileHeader3 = 0xD4
        };
        var original = new TileSnapshot(TileDataCompatibility.Capture(source), true);
        TileSnapshot restoredSnapshot;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(original.IsMaterialized);
                writer.Write(original.Data.Type);
                writer.Write(original.Data.Wall);
                writer.Write(original.Data.TileHeader);
                writer.Write(original.Data.FrameX);
                writer.Write(original.Data.FrameY);
                writer.Write(original.Data.Liquid);
                writer.Write(original.Data.Header);
                writer.Write(original.Data.Header2);
                writer.Write(original.Data.Header3);
            }
            stream.Position = 0;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                bool materialized = reader.ReadBoolean();
                TileData data = default(TileData);
                data.Type = reader.ReadUInt16();
                data.Wall = reader.ReadUInt16();
                data.TileHeader = reader.ReadUInt16();
                data.FrameX = reader.ReadInt16();
                data.FrameY = reader.ReadInt16();
                data.Liquid = reader.ReadByte();
                data.Header = reader.ReadByte();
                data.Header2 = reader.ReadByte();
                data.Header3 = reader.ReadByte();
                restoredSnapshot = new TileSnapshot(data, materialized);
                Assert(stream.Position == stream.Length, "The compact snapshot fixture must consume exactly its own bytes.");
            }
        }

        Assert(restoredSnapshot.IsMaterialized && restoredSnapshot.Data.Equals(original.Data), "The compact snapshot fixture must preserve every raw field and materialization state.");
        var restored = new Tile();
        TileDataCompatibility.Apply(restoredSnapshot.Data, restored);
        AssertSameRawState(source, restored);
    }

    private static void VerifyVanillaClearAndCopySemantics()
    {
        var source = new Tile
        {
            type = 321,
            wall = 42,
            sTileHeader = 0xFFFF,
            frameX = -18,
            frameY = 36,
            liquid = 255,
            bTileHeader = 3,
            bTileHeader2 = 4,
            bTileHeader3 = 5
        };

        TileData data = TileDataCompatibility.Capture(source);
        var expectedCopy = new Tile();
        expectedCopy.CopyFrom(source);
        TileData copy = default(TileData);
        copy.CopyFrom(data);
        var actualCopy = new Tile();
        TileDataCompatibility.Apply(copy, actualCopy);
        AssertSameRawState(expectedCopy, actualCopy);

        var expectedTileClear = new Tile();
        expectedTileClear.CopyFrom(source);
        expectedTileClear.ClearTile();
        TileData tileClear = TileDataCompatibility.Capture(source);
        tileClear.ClearTile();
        var actualTileClear = new Tile();
        TileDataCompatibility.Apply(tileClear, actualTileClear);
        AssertSameRawState(expectedTileClear, actualTileClear);

        var expectedFullClear = new Tile();
        expectedFullClear.CopyFrom(source);
        expectedFullClear.ClearEverything();
        TileData fullClear = TileDataCompatibility.Capture(source);
        fullClear.ClearEverything();
        var actualFullClear = new Tile();
        TileDataCompatibility.Apply(fullClear, actualFullClear);
        AssertSameRawState(expectedFullClear, actualFullClear);
    }

    private static void VerifyHeaderAccessors()
    {
        VerifyPrimaryHeaderBoolean("active", (tile, value) => tile.active(value), tile => tile.active(), (ref TileData data, bool value) => data.active(value), data => data.active());
        VerifyPrimaryHeaderBoolean("inActive", (tile, value) => tile.inActive(value), tile => tile.inActive(), (ref TileData data, bool value) => data.inActive(value), data => data.inActive());
        VerifyPrimaryHeaderBoolean("wire", (tile, value) => tile.wire(value), tile => tile.wire(), (ref TileData data, bool value) => data.wire(value), data => data.wire());
        VerifyPrimaryHeaderBoolean("wire2", (tile, value) => tile.wire2(value), tile => tile.wire2(), (ref TileData data, bool value) => data.wire2(value), data => data.wire2());
        VerifyPrimaryHeaderBoolean("wire3", (tile, value) => tile.wire3(value), tile => tile.wire3(), (ref TileData data, bool value) => data.wire3(value), data => data.wire3());
        VerifyPrimaryHeaderBoolean("halfBrick", (tile, value) => tile.halfBrick(value), tile => tile.halfBrick(), (ref TileData data, bool value) => data.halfBrick(value), data => data.halfBrick());
        VerifyPrimaryHeaderBoolean("actuator", (tile, value) => tile.actuator(value), tile => tile.actuator(), (ref TileData data, bool value) => data.actuator(value), data => data.actuator());
        VerifyPrimaryHeaderBoolean("fullbrightWall", (tile, value) => tile.fullbrightWall(value), tile => tile.fullbrightWall(), (ref TileData data, bool value) => data.fullbrightWall(value), data => data.fullbrightWall());
        VerifyPrimaryHeaderSlope();

        VerifyLiquidHeaderBoolean("lava", (tile, value) => tile.lava(value), tile => tile.lava(), (ref TileData data, bool value) => data.lava(value), data => data.lava());
        VerifyLiquidHeaderBoolean("honey", (tile, value) => tile.honey(value), tile => tile.honey(), (ref TileData data, bool value) => data.honey(value), data => data.honey());
        VerifyLiquidHeaderBoolean("shimmer", (tile, value) => tile.shimmer(value), tile => tile.shimmer(), (ref TileData data, bool value) => data.shimmer(value), data => data.shimmer());
        VerifyLiquidHeaderBoolean("wire4", (tile, value) => tile.wire4(value), tile => tile.wire4(), (ref TileData data, bool value) => data.wire4(value), data => data.wire4());
        VerifyLiquidType();
        VerifyTileAndWallColors();
        VerifySecondaryHeaders();
        VerifyMetadataResets();
        VerifyDerivedHeaderReaders();
        VerifyPaintAndBlockSemantics();
        VerifySelectiveClearSemantics();
    }

    private static void VerifyPrimaryHeaderBoolean(string name, TileBooleanSetter expectedSetter, TileBooleanGetter expectedGetter, TileDataBooleanSetter actualSetter, TileDataBooleanGetter actualGetter)
    {
        var tile = new Tile();
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            tile.sTileHeader = (ushort)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(expectedGetter(tile) == actualGetter(data), name + " getter diverged for primary header " + raw + ".");
            for (int value = 0; value < 2; value++)
            {
                bool enabled = value != 0;
                tile.sTileHeader = (ushort)raw;
                data = TileDataCompatibility.Capture(tile);
                expectedSetter(tile, enabled);
                actualSetter(ref data, enabled);
                Assert(TileDataCompatibility.Capture(tile).TileHeader == data.TileHeader, name + " setter diverged for primary header " + raw + ".");
            }
        }
    }

    private static void VerifyPrimaryHeaderSlope()
    {
        byte[] values = { 0, 1, 7, 8, byte.MaxValue };
        var tile = new Tile();
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            tile.sTileHeader = (ushort)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.slope() == data.slope(), "slope getter diverged for primary header " + raw + ".");
            foreach (byte value in values)
            {
                tile.sTileHeader = (ushort)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.slope(value);
                data.slope(value);
                Assert(TileDataCompatibility.Capture(tile).TileHeader == data.TileHeader, "slope setter diverged for primary header " + raw + ".");
            }
        }
    }

    private static void VerifyLiquidHeaderBoolean(string name, TileBooleanSetter expectedSetter, TileBooleanGetter expectedGetter, TileDataBooleanSetter actualSetter, TileDataBooleanGetter actualGetter)
    {
        var tile = new Tile();
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            tile.bTileHeader = (byte)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(expectedGetter(tile) == actualGetter(data), name + " getter diverged for liquid header " + raw + ".");
            for (int value = 0; value < 2; value++)
            {
                bool enabled = value != 0;
                tile.bTileHeader = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                expectedSetter(tile, enabled);
                actualSetter(ref data, enabled);
                Assert(TileDataCompatibility.Capture(tile).Header == data.Header, name + " setter diverged for liquid header " + raw + ".");
            }
        }
    }

    private static void VerifyLiquidType()
    {
        var tile = new Tile();
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            tile.bTileHeader = (byte)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.liquidType() == data.liquidType(), "liquidType getter diverged for liquid header " + raw + ".");
            for (int value = 0; value <= 4; value++)
            {
                tile.bTileHeader = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.liquidType(value);
                data.liquidType(value);
                Assert(TileDataCompatibility.Capture(tile).Header == data.Header, "liquidType setter diverged for liquid header " + raw + ".");
            }
        }
    }

    private static void VerifyTileAndWallColors()
    {
        byte[] values = { 0, 1, 31, 32, byte.MaxValue };
        var tile = new Tile();
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            tile.sTileHeader = (ushort)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.color() == data.color(), "color getter diverged for primary header " + raw + ".");
            foreach (byte value in values)
            {
                tile.sTileHeader = (ushort)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.color(value);
                data.color(value);
                Assert(TileDataCompatibility.Capture(tile).TileHeader == data.TileHeader, "color setter diverged for primary header " + raw + ".");
            }
        }

        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            tile.bTileHeader = (byte)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.wallColor() == data.wallColor(), "wallColor getter diverged for liquid header " + raw + ".");
            foreach (byte value in values)
            {
                tile.bTileHeader = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.wallColor(value);
                data.wallColor(value);
                Assert(TileDataCompatibility.Capture(tile).Header == data.Header, "wallColor setter diverged for liquid header " + raw + ".");
            }
        }
    }

    private static void VerifySecondaryHeaders()
    {
        VerifyHeader2FrameData();
        VerifyHeader3FlagsAndFrameData();
    }

    private static void VerifyHeader2FrameData()
    {
        int[] frameValues = { -36, -1, 0, 35, 36, 540, int.MaxValue };
        byte[] numberValues = { 0, 1, 3, 4, byte.MaxValue };
        var tile = new Tile();
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            tile.bTileHeader2 = (byte)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.wallFrameX() == data.wallFrameX() && tile.frameNumber() == data.frameNumber() && tile.wallFrameNumber() == data.wallFrameNumber(), "Header2 getters diverged for " + raw + ".");
            foreach (int value in frameValues)
            {
                tile.bTileHeader2 = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.wallFrameX(value);
                data.wallFrameX(value);
                Assert(TileDataCompatibility.Capture(tile).Header2 == data.Header2, "wallFrameX setter diverged for Header2 " + raw + ".");
            }
            foreach (byte value in numberValues)
            {
                tile.bTileHeader2 = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.frameNumber(value);
                data.frameNumber(value);
                Assert(TileDataCompatibility.Capture(tile).Header2 == data.Header2, "frameNumber setter diverged for Header2 " + raw + ".");

                tile.bTileHeader2 = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.wallFrameNumber(value);
                data.wallFrameNumber(value);
                Assert(TileDataCompatibility.Capture(tile).Header2 == data.Header2, "wallFrameNumber setter diverged for Header2 " + raw + ".");
            }
        }
    }

    private static void VerifyHeader3FlagsAndFrameData()
    {
        int[] frameValues = { -36, -1, 0, 35, 36, 252, int.MaxValue };
        var tile = new Tile();
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            tile.bTileHeader3 = (byte)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.wallFrameY() == data.wallFrameY(), "wallFrameY getter diverged for Header3 " + raw + ".");
            foreach (int value in frameValues)
            {
                tile.bTileHeader3 = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                tile.wallFrameY(value);
                data.wallFrameY(value);
                Assert(TileDataCompatibility.Capture(tile).Header3 == data.Header3, "wallFrameY setter diverged for Header3 " + raw + ".");
            }
        }

        VerifyHeader3Boolean("checkingLiquid", (tile, value) => tile.checkingLiquid(value), tile => tile.checkingLiquid(), (ref TileData data, bool value) => data.checkingLiquid(value), data => data.checkingLiquid());
        VerifyHeader3Boolean("skipLiquid", (tile, value) => tile.skipLiquid(value), tile => tile.skipLiquid(), (ref TileData data, bool value) => data.skipLiquid(value), data => data.skipLiquid());
        VerifyHeader3Boolean("invisibleBlock", (tile, value) => tile.invisibleBlock(value), tile => tile.invisibleBlock(), (ref TileData data, bool value) => data.invisibleBlock(value), data => data.invisibleBlock());
        VerifyHeader3Boolean("invisibleWall", (tile, value) => tile.invisibleWall(value), tile => tile.invisibleWall(), (ref TileData data, bool value) => data.invisibleWall(value), data => data.invisibleWall());
        VerifyHeader3Boolean("fullbrightBlock", (tile, value) => tile.fullbrightBlock(value), tile => tile.fullbrightBlock(), (ref TileData data, bool value) => data.fullbrightBlock(value), data => data.fullbrightBlock());
    }

    private static void VerifyHeader3Boolean(string name, TileBooleanSetter expectedSetter, TileBooleanGetter expectedGetter, TileDataBooleanSetter actualSetter, TileDataBooleanGetter actualGetter)
    {
        var tile = new Tile();
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            tile.bTileHeader3 = (byte)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(expectedGetter(tile) == actualGetter(data), name + " getter diverged for Header3 " + raw + ".");
            for (int value = 0; value < 2; value++)
            {
                bool enabled = value != 0;
                tile.bTileHeader3 = (byte)raw;
                data = TileDataCompatibility.Capture(tile);
                expectedSetter(tile, enabled);
                actualSetter(ref data, enabled);
                Assert(TileDataCompatibility.Capture(tile).Header3 == data.Header3, name + " setter diverged for Header3 " + raw + ".");
            }
        }
    }

    private static void VerifyMetadataResets()
    {
        var source = new Tile
        {
            type = 321,
            wall = 42,
            sTileHeader = 0xFFFF,
            frameX = -18,
            frameY = 36,
            liquid = 255,
            bTileHeader = 3,
            bTileHeader2 = 4,
            bTileHeader3 = 5
        };
        var expected = new Tile();
        expected.CopyFrom(source);
        expected.ResetToType(654);
        TileData actual = TileDataCompatibility.Capture(source);
        actual.ResetToType(654);
        var restored = new Tile();
        TileDataCompatibility.Apply(actual, restored);
        AssertSameRawState(expected, restored);

        expected.CopyFrom(source);
        ClearMetadataMethod.Invoke(expected, null);
        actual = TileDataCompatibility.Capture(source);
        actual.ClearMetadata();
        TileDataCompatibility.Apply(actual, restored);
        AssertSameRawState(expected, restored);
    }

    private static void VerifyDerivedHeaderReaders()
    {
        var tile = new Tile();
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            tile.sTileHeader = (ushort)raw;
            TileData data = TileDataCompatibility.Capture(tile);
            Assert(tile.nactive() == data.nactive(), "nactive diverged for primary header " + raw + ".");
            Assert(tile.topSlope() == data.topSlope() && tile.bottomSlope() == data.bottomSlope() && tile.leftSlope() == data.leftSlope() && tile.rightSlope() == data.rightSlope(), "Slope orientation diverged for primary header " + raw + ".");

            tile.bTileHeader = 0;
            data = TileDataCompatibility.Capture(tile);
            Assert(tile.anyWire() == data.anyWire(), "anyWire diverged without wire4 for primary header " + raw + ".");
            tile.bTileHeader = 0x80;
            data = TileDataCompatibility.Capture(tile);
            Assert(tile.anyWire() == data.anyWire(), "anyWire diverged with wire4 for primary header " + raw + ".");
        }

        byte[] liquidAmounts = { 0, 1, byte.MaxValue };
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            foreach (byte amount in liquidAmounts)
            {
                tile.bTileHeader = (byte)raw;
                tile.liquid = amount;
                TileData data = TileDataCompatibility.Capture(tile);
                Assert(tile.water() == data.water() && tile.anyWater() == data.anyWater() && tile.anyLava() == data.anyLava() && tile.anyHoney() == data.anyHoney() && tile.anyShimmer() == data.anyShimmer(), "Liquid predicates diverged for liquid header " + raw + ".");
            }
        }
    }

    private static void VerifyPaintAndBlockSemantics()
    {
        var source = new Tile
        {
            type = 321,
            wall = 42,
            sTileHeader = 0xFFFF,
            frameX = -18,
            frameY = 36,
            liquid = 255,
            bTileHeader = 0xFF,
            bTileHeader2 = 4,
            bTileHeader3 = 0xFF
        };
        var expected = new Tile();
        expected.CopyFrom(source);
        expected.ClearTileAndPaint();
        TileData actual = TileDataCompatibility.Capture(source);
        actual.ClearTileAndPaint();
        var restored = new Tile();
        TileDataCompatibility.Apply(actual, restored);
        AssertSameRawState(expected, restored);

        expected.CopyFrom(source);
        expected.ClearBlockPaintAndCoating();
        actual = TileDataCompatibility.Capture(source);
        actual.ClearBlockPaintAndCoating();
        TileDataCompatibility.Apply(actual, restored);
        AssertSameRawState(expected, restored);

        expected.CopyFrom(source);
        expected.ClearWallPaintAndCoating();
        actual = TileDataCompatibility.Capture(source);
        actual.ClearWallPaintAndCoating();
        TileDataCompatibility.Apply(actual, restored);
        AssertSameRawState(expected, restored);

        var destination = new Tile
        {
            type = 1,
            wall = 2,
            sTileHeader = 0,
            bTileHeader = 0,
            bTileHeader2 = 3,
            bTileHeader3 = 0
        };
        destination.CopyPaintAndCoating(source);
        actual = TileDataCompatibility.Capture(new Tile
        {
            type = 1,
            wall = 2,
            sTileHeader = 0,
            bTileHeader = 0,
            bTileHeader2 = 3,
            bTileHeader3 = 0
        });
        actual.CopyPaintAndCoating(TileDataCompatibility.Capture(source));
        TileDataCompatibility.Apply(actual, restored);
        AssertSameRawState(destination, restored);

        var tile = new Tile();
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            tile.sTileHeader = (ushort)raw;
            actual = TileDataCompatibility.Capture(tile);
            Assert(tile.blockType() == actual.blockType(), "blockType diverged for primary header " + raw + ".");
        }
    }

    private static void VerifySelectiveClearSemantics()
    {
        TileDataType[] masks =
        {
            0,
            TileDataType.Tile,
            TileDataType.TilePaint,
            TileDataType.Wall,
            TileDataType.WallPaint,
            TileDataType.Liquid,
            TileDataType.Wiring,
            TileDataType.Actuator,
            TileDataType.Slope,
            TileDataType.Tile | TileDataType.TilePaint | TileDataType.Slope,
            ~(TileDataType.Wiring | TileDataType.Actuator),
            TileDataType.All
        };
        foreach (TileDataType mask in masks)
        {
            var expected = new Tile
            {
                type = 321,
                wall = 42,
                sTileHeader = 0xFFFF,
                frameX = -18,
                frameY = 36,
                liquid = 255,
                bTileHeader = 0xFF,
                bTileHeader2 = 0xFF,
                bTileHeader3 = 0xFF
            };
            TileData actual = TileDataCompatibility.Capture(expected);
            expected.Clear(mask);
            actual.Clear((TileDataMask)(int)mask);
            var restored = new Tile();
            TileDataCompatibility.Apply(actual, restored);
            AssertSameRawState(expected, restored);
        }
    }

    private delegate void TileBooleanSetter(Tile tile, bool value);
    private delegate bool TileBooleanGetter(Tile tile);
    private delegate void TileDataBooleanSetter(ref TileData data, bool value);
    private delegate bool TileDataBooleanGetter(TileData data);

    private static void AssertSameRawState(Tile expected, Tile actual)
    {
        Assert(expected.type == actual.type && expected.wall == actual.wall && expected.sTileHeader == actual.sTileHeader, "Tile type, wall, and primary header must round-trip.");
        Assert(expected.frameX == actual.frameX && expected.frameY == actual.frameY && expected.liquid == actual.liquid, "Tile frames and liquid must round-trip.");
        Assert(expected.bTileHeader == actual.bTileHeader && expected.bTileHeader2 == actual.bTileHeader2 && expected.bTileHeader3 == actual.bTileHeader3, "All secondary tile headers must round-trip.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
