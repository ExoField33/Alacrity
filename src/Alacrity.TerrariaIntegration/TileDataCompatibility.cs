#nullable enable
using System;
using Terraria;

namespace AlacrityTerraria
{
    internal static class TileDataCompatibility
    {
        // Save/network parity depends on preserving Terraria.Tile's raw fields exactly, including header bits.
        internal static TileData Capture(Tile tile)
        {
            if (tile is null)
                throw new ArgumentNullException(nameof(tile));

            return new TileData
            {
                Type = tile.type,
                Wall = tile.wall,
                TileHeader = tile.sTileHeader,
                FrameX = tile.frameX,
                FrameY = tile.frameY,
                Liquid = tile.liquid,
                Header = tile.bTileHeader,
                Header2 = tile.bTileHeader2,
                Header3 = tile.bTileHeader3
            };
        }

        internal static void Apply(in TileData data, Tile tile)
        {
            if (tile is null)
                throw new ArgumentNullException(nameof(tile));

            tile.type = data.Type;
            tile.wall = data.Wall;
            tile.sTileHeader = data.TileHeader;
            tile.frameX = data.FrameX;
            tile.frameY = data.FrameY;
            tile.liquid = data.Liquid;
            tile.bTileHeader = data.Header;
            tile.bTileHeader2 = data.Header2;
            tile.bTileHeader3 = data.Header3;
        }
    }
}
#nullable disable
