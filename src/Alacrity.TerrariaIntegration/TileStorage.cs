#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AlacrityTerraria
{
    [Flags]
    internal enum TileDataMask
    {
        Tile = 1,
        TilePaint = 2,
        Wall = 4,
        WallPaint = 8,
        Liquid = 0x10,
        Wiring = 0x20,
        Actuator = 0x40,
        Slope = 0x80,
        All = 0xFF
    }

    // This representation stays internal until every Terraria Tile[,] use has a verified migration.
    [StructLayout(LayoutKind.Sequential)]
    internal struct TileData : IEquatable<TileData>
    {
        internal ushort Type;
        internal ushort Wall;
        internal ushort TileHeader;
        internal short FrameX;
        internal short FrameY;
        internal byte Liquid;
        internal byte Header;
        internal byte Header2;
        internal byte Header3;

        internal void ClearEverything()
        {
            this = default(TileData);
        }

        // Matches Terraria.Tile.ClearTile: leave wall, liquid, paint, wiring, and frame state intact.
        internal void ClearTile()
        {
            ClearSlope();
            TileHeader = (ushort)(TileHeader & 0xFFDF);
            TileHeader = (ushort)(TileHeader & 0xFFBF);
        }

        internal void ClearTileAndPaint()
        {
            ClearTile();
            ClearBlockPaintAndCoating();
        }

        internal void ClearSlope()
        {
            TileHeader = (ushort)(TileHeader & 0x8FFF);
            TileHeader = (ushort)(TileHeader & 0xFBFF);
        }

        internal void CopyFrom(in TileData from)
        {
            this = from;
        }

        internal void liquidType(int liquidType)
        {
            switch (liquidType)
            {
                case 0: Header = (byte)(Header & 0x9F); break;
                case 1: lava(true); break;
                case 2: honey(true); break;
                case 3: shimmer(true); break;
            }
        }

        internal byte liquidType() { return (byte)((Header & 0x60) >> 5); }
        internal bool nactive() { return (TileHeader & 0x60) == 0x20; }

        internal void ResetToType(ushort type)
        {
            Liquid = 0;
            TileHeader = 0x20;
            Header = Header2 = Header3 = 0;
            FrameX = FrameY = 0;
            Type = type;
        }

        internal void ClearMetadata()
        {
            Liquid = 0;
            TileHeader = 0;
            Header = Header2 = Header3 = 0;
            FrameX = FrameY = 0;
        }

        internal bool topSlope() { byte value = slope(); return value == 1 || value == 2; }
        internal bool bottomSlope() { byte value = slope(); return value == 3 || value == 4; }
        internal bool leftSlope() { byte value = slope(); return value == 2 || value == 4; }
        internal bool rightSlope() { byte value = slope(); return value == 1 || value == 3; }
        internal int blockType()
        {
            if (halfBrick())
                return 1;
            int value = slope();
            return value > 0 ? value + 1 : value;
        }

        internal byte wallColor() { return (byte)(Header & 0x1F); }
        internal void wallColor(byte wallColor) { Header = (byte)((Header & 0xE0) | wallColor); }

        internal bool lava() { return (Header & 0x60) == 0x20; }
        internal void lava(bool value) { Header = value ? (byte)((Header & 0x9F) | 0x20) : (byte)(Header & 0xDF); }
        internal bool honey() { return (Header & 0x60) == 0x40; }
        internal void honey(bool value) { Header = value ? (byte)((Header & 0x9F) | 0x40) : (byte)(Header & 0xBF); }
        internal bool shimmer() { return (Header & 0x60) == 0x60; }
        internal void shimmer(bool value) { Header = value ? (byte)((Header & 0x9F) | 0x60) : (byte)(Header & 0x9F); }
        internal bool water() { return liquidType() == 0; }
        internal bool anyWater() { return Liquid > 0 && water(); }
        internal bool anyLava() { return Liquid > 0 && lava(); }
        internal bool anyHoney() { return Liquid > 0 && honey(); }
        internal bool anyShimmer() { return Liquid > 0 && shimmer(); }

        internal bool wire4() { return (Header & 0x80) != 0; }
        internal void wire4(bool value) { Header = value ? (byte)(Header | 0x80) : (byte)(Header & 0x7F); }

        internal int wallFrameX() { return (Header2 & 0x0F) * 36; }
        internal void wallFrameX(int value) { Header2 = (byte)((Header2 & 0xF0) | ((value / 36) & 0x0F)); }
        internal byte frameNumber() { return (byte)((Header2 & 0x30) >> 4); }
        internal void frameNumber(byte value) { Header2 = (byte)((Header2 & 0xCF) | ((value & 3) << 4)); }
        internal byte wallFrameNumber() { return (byte)((Header2 & 0xC0) >> 6); }
        internal void wallFrameNumber(byte value) { Header2 = (byte)((Header2 & 0x3F) | ((value & 3) << 6)); }
        internal int wallFrameY() { return (Header3 & 7) * 36; }
        internal void wallFrameY(int value) { Header3 = (byte)((Header3 & 0xF8) | ((value / 36) & 7)); }

        internal bool checkingLiquid() { return (Header3 & 8) != 0; }
        internal void checkingLiquid(bool value) { Header3 = value ? (byte)(Header3 | 8) : (byte)(Header3 & 0xF7); }
        internal bool skipLiquid() { return (Header3 & 0x10) != 0; }
        internal void skipLiquid(bool value) { Header3 = value ? (byte)(Header3 | 0x10) : (byte)(Header3 & 0xEF); }
        internal bool invisibleBlock() { return (Header3 & 0x20) != 0; }
        internal void invisibleBlock(bool value) { Header3 = value ? (byte)(Header3 | 0x20) : (byte)(Header3 & 0xDF); }
        internal bool invisibleWall() { return (Header3 & 0x40) != 0; }
        internal void invisibleWall(bool value) { Header3 = value ? (byte)(Header3 | 0x40) : (byte)(Header3 & 0xBF); }
        internal bool fullbrightBlock() { return (Header3 & 0x80) != 0; }
        internal void fullbrightBlock(bool value) { Header3 = value ? (byte)(Header3 | 0x80) : (byte)(Header3 & 0x7F); }

        internal byte color() { return (byte)(TileHeader & 0x1F); }
        internal void color(byte value) { TileHeader = (ushort)((TileHeader & 0xFFE0) | value); }
        internal bool active() { return (TileHeader & 0x20) != 0; }
        internal void active(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x20) : (ushort)(TileHeader & 0xFFDF); }
        internal bool inActive() { return (TileHeader & 0x40) != 0; }
        internal void inActive(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x40) : (ushort)(TileHeader & 0xFFBF); }
        internal bool wire() { return (TileHeader & 0x80) != 0; }
        internal void wire(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x80) : (ushort)(TileHeader & 0xFF7F); }
        internal bool wire2() { return (TileHeader & 0x100) != 0; }
        internal void wire2(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x100) : (ushort)(TileHeader & 0xFEFF); }
        internal bool wire3() { return (TileHeader & 0x200) != 0; }
        internal void wire3(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x200) : (ushort)(TileHeader & 0xFDFF); }
        internal bool halfBrick() { return (TileHeader & 0x400) != 0; }
        internal void halfBrick(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x400) : (ushort)(TileHeader & 0xFBFF); }
        internal bool actuator() { return (TileHeader & 0x800) != 0; }
        internal void actuator(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x800) : (ushort)(TileHeader & 0xF7FF); }
        internal byte slope() { return (byte)((TileHeader & 0x7000) >> 12); }
        internal void slope(byte value) { TileHeader = (ushort)((TileHeader & 0x8FFF) | ((value & 7) << 12)); }
        internal bool fullbrightWall() { return (TileHeader & 0x8000) != 0; }
        internal void fullbrightWall(bool value) { TileHeader = value ? (ushort)(TileHeader | 0x8000) : (ushort)(TileHeader & 0x7FFF); }
        internal bool anyWire() { return (TileHeader & 0x380) != 0 || (Header & 0x80) != 0; }

        internal void CopyPaintAndCoating(in TileData other)
        {
            color(other.color());
            invisibleBlock(other.invisibleBlock());
            fullbrightBlock(other.fullbrightBlock());
        }

        internal void ClearBlockPaintAndCoating()
        {
            color(0);
            fullbrightBlock(false);
            invisibleBlock(false);
        }

        internal void ClearWallPaintAndCoating()
        {
            wallColor(0);
            fullbrightWall(false);
            invisibleWall(false);
        }

        internal void Clear(TileDataMask types)
        {
            if ((types & TileDataMask.Tile) != 0)
            {
                Type = 0;
                active(false);
                FrameX = 0;
                FrameY = 0;
            }
            if ((types & TileDataMask.Wall) != 0)
            {
                Wall = 0;
                wallFrameX(0);
                wallFrameY(0);
            }
            if ((types & TileDataMask.TilePaint) != 0)
                ClearBlockPaintAndCoating();
            if ((types & TileDataMask.WallPaint) != 0)
                ClearWallPaintAndCoating();
            if ((types & TileDataMask.Liquid) != 0)
            {
                Liquid = 0;
                liquidType(0);
                checkingLiquid(false);
            }
            if ((types & TileDataMask.Slope) != 0)
                ClearSlope();
            if ((types & TileDataMask.Wiring) != 0)
            {
                wire(false);
                wire2(false);
                wire3(false);
                wire4(false);
            }
            if ((types & TileDataMask.Actuator) != 0)
            {
                actuator(false);
                inActive(false);
            }
        }

        public bool Equals(TileData other)
        {
            return Type == other.Type && Wall == other.Wall && TileHeader == other.TileHeader &&
                   FrameX == other.FrameX && FrameY == other.FrameY && Liquid == other.Liquid &&
                   Header == other.Header && Header2 == other.Header2 && Header3 == other.Header3;
        }

        public override bool Equals(object? obj)
        {
            return obj is TileData && Equals((TileData)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Type;
                hash = (hash * 397) ^ Wall;
                hash = (hash * 397) ^ TileHeader;
                hash = (hash * 397) ^ FrameX;
                hash = (hash * 397) ^ FrameY;
                hash = (hash * 397) ^ Liquid;
                hash = (hash * 397) ^ Header;
                hash = (hash * 397) ^ Header2;
                return (hash * 397) ^ Header3;
            }
        }
    }

    internal struct TileSnapshot
    {
        internal TileSnapshot(TileData data, bool isMaterialized)
        {
            Data = data;
            IsMaterialized = isMaterialized;
        }

        internal TileData Data { get; private set; }
        internal bool IsMaterialized { get; private set; }
    }

    internal sealed class AlacrityTileMap
    {
        private readonly TileData[] data;
        private readonly uint[] materialized;
        private readonly int[] versions;
        private readonly Dictionary<long, TileStandalone> displacedReferences;
        private readonly Dictionary<long, TileReferenceTarget> aliasedReferences;

        internal AlacrityTileMap(int width, int height)
            : this(width, height, 0)
        {
        }

        internal AlacrityTileMap(int width, int height, long generation)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            Generation = generation;
            int count = checked(width * height);
            data = new TileData[count];
            materialized = new uint[checked((count + 31) / 32)];
            versions = new int[count];
            displacedReferences = new Dictionary<long, TileStandalone>();
            aliasedReferences = new Dictionary<long, TileReferenceTarget>();
        }

        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int Count { get { return data.Length; } }
        internal long Generation { get; private set; }

        internal TileData GetDataUnchecked(int x, int y)
        {
            return data[GetIndexUnchecked(x, y)];
        }

        internal ref TileData EnsureMaterialized(int x, int y)
        {
            int index = GetIndex(x, y);
            SetMaterialized(index, true);
            return ref data[index];
        }

        internal TileReference GetReference(int x, int y)
        {
            int index = GetIndex(x, y);
            return IsMaterialized(index) ? new TileReference(this, index, versions[index]) : default(TileReference);
        }

        internal TileReference GetOrCreateReference(int x, int y)
        {
            int index = GetIndex(x, y);
            SetMaterialized(index, true);
            return new TileReference(this, index, versions[index]);
        }

        internal bool IsMaterialized(int x, int y)
        {
            return IsMaterialized(GetIndex(x, y));
        }

        internal TileSnapshot GetSnapshot(int x, int y)
        {
            int index = GetIndex(x, y);
            return new TileSnapshot(data[index], IsMaterialized(index));
        }

        internal void SetSnapshot(int x, int y, TileSnapshot snapshot)
        {
            int index = GetIndex(x, y);
            ReplaceCurrentReference(index);
            data[index] = snapshot.Data;
            SetMaterialized(index, snapshot.IsMaterialized);
        }

        // Array assignment preserves the source Tile object's identity; aliases stay sparse and never enter TileData[].
        internal void SetReference(int x, int y, TileReference source)
        {
            int index = GetIndex(x, y);
            ReplaceCurrentReference(index);
            if (source.IsNull)
            {
                data[index].ClearEverything();
                SetMaterialized(index, false);
                return;
            }

            aliasedReferences.Add(GetReferenceKey(index, versions[index]), source.GetTarget());
            SetMaterialized(index, true);
        }

        // Content copy is explicit; it is not a replacement for vanilla Tile reference assignment.
        internal void CopyTileData(int sourceX, int sourceY, int destinationX, int destinationY)
        {
            SetSnapshot(destinationX, destinationY, GetSnapshot(sourceX, sourceY));
        }

        internal void ClearTile(int x, int y)
        {
            int index = GetIndex(x, y);
            data[index].ClearTile();
            SetMaterialized(index, true);
        }

        internal void ClearEverything(int x, int y)
        {
            int index = GetIndex(x, y);
            data[index].ClearEverything();
            SetMaterialized(index, true);
        }

        internal void ClearTileData(int x, int y, TileDataMask types)
        {
            int index = GetIndex(x, y);
            data[index].Clear(types);
            SetMaterialized(index, true);
        }

        internal void CopyPaintAndCoating(int sourceX, int sourceY, int destinationX, int destinationY)
        {
            int sourceIndex = GetIndex(sourceX, sourceY);
            int destinationIndex = GetIndex(destinationX, destinationY);
            data[destinationIndex].CopyPaintAndCoating(data[sourceIndex]);
            SetMaterialized(destinationIndex, true);
        }

        internal void UnmaterializeTile(int x, int y)
        {
            int index = GetIndex(x, y);
            ReplaceCurrentReference(index);
            data[index].ClearEverything();
            SetMaterialized(index, false);
        }

        internal void ClearAll()
        {
            Array.Clear(data, 0, data.Length);
            Array.Clear(materialized, 0, materialized.Length);
            Array.Clear(versions, 0, versions.Length);
            displacedReferences.Clear();
            aliasedReferences.Clear();
        }

        internal void ClearRegion(int x, int y, int width, int height)
        {
            ValidateRegion(x, y, width, height);
            for (int row = 0; row < height; row++)
            {
                int start = GetIndexUnchecked(x, y + row);
                for (int column = 0; column < width; column++)
                {
                    ReplaceCurrentReference(start + column);
                    data[start + column].ClearEverything();
                    SetMaterialized(start + column, false);
                }
            }
        }

        internal void FillRegion(int x, int y, int width, int height, TileData value)
        {
            ValidateRegion(x, y, width, height);
            for (int row = 0; row < height; row++)
            {
                int start = GetIndexUnchecked(x, y + row);
                for (int column = 0; column < width; column++)
                {
                    int index = start + column;
                    data[index] = value;
                    SetMaterialized(index, true);
                }
            }
        }

        internal void CopyRegion(int sourceX, int sourceY, int width, int height, int destinationX, int destinationY)
        {
            ValidateRegion(sourceX, sourceY, width, height);
            ValidateRegion(destinationX, destinationY, width, height);

            int startRow = 0;
            int endRow = height;
            int step = 1;
            if (destinationY > sourceY)
            {
                startRow = height - 1;
                endRow = -1;
                step = -1;
            }

            for (int row = startRow; row != endRow; row += step)
            {
                int source = GetIndexUnchecked(sourceX, sourceY + row);
                int destination = GetIndexUnchecked(destinationX, destinationY + row);
                Array.Copy(data, source, data, destination, width);

                int startColumn = 0;
                int endColumn = width;
                int columnStep = 1;
                if (sourceY + row == destinationY + row && destinationX > sourceX)
                {
                    startColumn = width - 1;
                    endColumn = -1;
                    columnStep = -1;
                }

                for (int column = startColumn; column != endColumn; column += columnStep)
                    SetMaterialized(destination + column, IsMaterialized(source + column));
            }
        }

        private int GetIndex(int x, int y)
        {
            if ((uint)x >= (uint)Width)
                throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(y));
            return GetIndexUnchecked(x, y);
        }

        internal void ValidateCoordinates(int x, int y)
        {
            GetIndex(x, y);
        }

        private int GetIndexUnchecked(int x, int y)
        {
            return x + y * Width;
        }

        private void ValidateRegion(int x, int y, int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || width > Width - x || height > Height - y)
                throw new ArgumentOutOfRangeException("region");
        }

        private bool IsMaterialized(int index)
        {
            return (materialized[index >> 5] & (1u << (index & 31))) != 0;
        }

        internal ref TileData GetDataByIndex(int index)
        {
            return ref data[index];
        }

        internal ref TileData GetDataForReference(int index, int version)
        {
            long key = GetReferenceKey(index, version);
            if (aliasedReferences.TryGetValue(key, out TileReferenceTarget? alias) && alias != null)
                return ref alias.GetData();
            if (versions[index] == version)
                return ref data[index];
            if (displacedReferences.TryGetValue(key, out TileStandalone? displaced))
                return ref displaced.Data;
            throw new InvalidOperationException("Tile reference does not belong to the active map state.");
        }

        private void SetMaterialized(int index, bool value)
        {
            int word = index >> 5;
            uint mask = 1u << (index & 31);
            if (value)
                materialized[word] |= mask;
            else
                materialized[word] &= ~mask;
        }

        private void ReplaceCurrentReference(int index)
        {
            if (!IsMaterialized(index))
                return;

            long key = GetReferenceKey(index, versions[index]);
            if (!aliasedReferences.ContainsKey(key))
                displacedReferences.Add(key, new TileStandalone { Data = data[index] });
            versions[index] = checked(versions[index] + 1);
        }

        private static long GetReferenceKey(int index, int version)
        {
            return ((long)index << 32) | (uint)version;
        }
    }

    // A copied reference retains map or standalone identity without a raw pointer into managed tile data.
    internal readonly struct TileReference
    {
        private readonly AlacrityTileMap? map;
        private readonly int index;
        private readonly int version;
        private readonly TileStandalone? standalone;

        internal TileReference(AlacrityTileMap map, int index, int version)
        {
            this.map = map;
            this.index = index;
            this.version = version;
            standalone = null;
        }

        private TileReference(TileStandalone standalone)
        {
            map = null;
            index = 0;
            version = 0;
            this.standalone = standalone;
        }

        internal bool IsNull { get { return map == null && standalone == null; } }

        internal static TileReference CreateStandalone()
        {
            return new TileReference(new TileStandalone());
        }

        internal static TileReference CreateCopy(TileReference source)
        {
            var copy = new TileStandalone();
            if (!source.IsNull)
                copy.Data = source.GetData();
            return new TileReference(copy);
        }

        internal ref TileData GetData()
        {
            if (map != null)
                return ref map.GetDataForReference(index, version);
            if (standalone != null)
                return ref standalone.Data;
            throw new NullReferenceException("Tile reference is null.");
        }

        internal TileReferenceTarget GetTarget()
        {
            if (map != null)
                return new TileReferenceTarget(map, index, version);
            if (standalone != null)
                return new TileReferenceTarget(standalone);
            throw new NullReferenceException("Tile reference is null.");
        }
    }

    internal sealed class TileReferenceTarget
    {
        private readonly AlacrityTileMap? map;
        private readonly int index;
        private readonly int version;
        private readonly TileStandalone? standalone;

        internal TileReferenceTarget(AlacrityTileMap map, int index, int version)
        {
            this.map = map;
            this.index = index;
            this.version = version;
            standalone = null;
        }

        internal TileReferenceTarget(TileStandalone standalone)
        {
            map = null;
            index = 0;
            version = 0;
            this.standalone = standalone;
        }

        internal ref TileData GetData()
        {
            if (map != null)
                return ref map.GetDataForReference(index, version);
            if (standalone != null)
                return ref standalone.Data;
            throw new InvalidOperationException("Tile reference target has no backing state.");
        }
    }

    internal sealed class TileStandalone
    {
        internal TileData Data;
    }

    // The live lowerer redirects verified Tile field instructions to this compact handle surface.
    internal static class TileReferenceRuntime
    {
        internal static bool IsNull(TileReference tile) { return tile.IsNull; }
        internal static TileReference Create() { return TileReference.CreateStandalone(); }
        internal static TileReference CreateCopy(TileReference source) { return TileReference.CreateCopy(source); }

        internal static ushort GetTypeValue(TileReference tile) { return tile.GetData().Type; }
        internal static void SetTypeValue(TileReference tile, ushort value) { tile.GetData().Type = value; }
        internal static ushort GetWall(TileReference tile) { return tile.GetData().Wall; }
        internal static void SetWall(TileReference tile, ushort value) { tile.GetData().Wall = value; }
        internal static byte GetLiquid(TileReference tile) { return tile.GetData().Liquid; }
        internal static void SetLiquid(TileReference tile, byte value) { tile.GetData().Liquid = value; }
        internal static ushort GetTileHeader(TileReference tile) { return tile.GetData().TileHeader; }
        internal static void SetTileHeader(TileReference tile, ushort value) { tile.GetData().TileHeader = value; }
        internal static byte GetHeader(TileReference tile) { return tile.GetData().Header; }
        internal static void SetHeader(TileReference tile, byte value) { tile.GetData().Header = value; }
        internal static byte GetHeader2(TileReference tile) { return tile.GetData().Header2; }
        internal static void SetHeader2(TileReference tile, byte value) { tile.GetData().Header2 = value; }
        internal static byte GetHeader3(TileReference tile) { return tile.GetData().Header3; }
        internal static void SetHeader3(TileReference tile, byte value) { tile.GetData().Header3 = value; }
        internal static short GetFrameX(TileReference tile) { return tile.GetData().FrameX; }
        internal static void SetFrameX(TileReference tile, short value) { tile.GetData().FrameX = value; }
        internal static short GetFrameY(TileReference tile) { return tile.GetData().FrameY; }
        internal static void SetFrameY(TileReference tile, short value) { tile.GetData().FrameY = value; }
    }

    // Handles are transient map coordinates; the generation check prevents old-world access after reset or reconnect.
    internal readonly struct TileHandle
    {
        private readonly AlacrityTileStorageHost owner;
        private readonly long generation;
        private readonly int x;
        private readonly int y;

        internal TileHandle(AlacrityTileStorageHost owner, long generation, int x, int y)
        {
            this.owner = owner;
            this.generation = generation;
            this.x = x;
            this.y = y;
        }

        internal TileSnapshot GetSnapshot()
        {
            return owner.GetCurrentMap(generation).GetSnapshot(x, y);
        }

        internal void SetSnapshot(TileSnapshot snapshot)
        {
            owner.GetCurrentMap(generation).SetSnapshot(x, y, snapshot);
        }

        internal ref TileData EnsureMaterialized()
        {
            return ref owner.GetCurrentMap(generation).EnsureMaterialized(x, y);
        }
    }

    internal sealed class AlacrityTileStorageHost
    {
        private AlacrityTileMap? current;
        private long generation;

        internal long Generation { get { return generation; } }

        internal void Initialize(int width, int height)
        {
            current = new AlacrityTileMap(width, height, checked(generation + 1));
            generation = current.Generation;
        }

        internal void Reset()
        {
            current = null;
            generation = checked(generation + 1);
        }

        internal TileHandle GetHandle(int x, int y)
        {
            AlacrityTileMap map = RequireCurrentMap();
            map.ValidateCoordinates(x, y);
            return new TileHandle(this, generation, x, y);
        }

        internal AlacrityTileMap GetCurrentMap(long expectedGeneration)
        {
            AlacrityTileMap map = RequireCurrentMap();
            if (expectedGeneration != generation)
                throw new InvalidOperationException("Tile handle belongs to an unloaded world.");
            return map;
        }

        private AlacrityTileMap RequireCurrentMap()
        {
            return current ?? throw new InvalidOperationException("No tile map is active.");
        }
    }
}
#nullable disable
