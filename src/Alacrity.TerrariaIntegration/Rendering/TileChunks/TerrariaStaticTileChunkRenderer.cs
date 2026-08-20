using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.Testing;
using Terraria.Utilities;

namespace AlacrityTerraria.Rendering.TileChunks;

/// <summary>
/// A conservative 20 by 20 tile descriptor cache for plain, static blocks. It deliberately
/// reuses Terraria's <see cref="Terraria.Graphics.TileBatch"/> rather than caching a lit render
/// target: light, visibility, and tile-frame effects must continue to be evaluated live.
/// Everything outside the audited basic-block subset returns false and remains in TileDrawing.
/// </summary>
internal static class TerrariaStaticTileChunkRenderer
{
    private const int ChunkTileSize = 20;
    private const int ChunkSlotWidth = 32;
    private const int ChunkSlotHeight = 16;
    private const int ChunkSlotMaskX = ChunkSlotWidth - 1;
    private const int ChunkSlotMaskY = ChunkSlotHeight - 1;
    private const int TilesPerChunk = ChunkTileSize * ChunkTileSize;

    private static readonly ChunkSlot[] Slots = CreateSlots();
    private static Vector3[] FourLightSlices = new Vector3[4];
    private static Vector3[] NineLightSlices = new Vector3[9];
    private static readonly object EmitterResolutionGate = new object();

    private static Tile[,] observedTileMap;
    private static EmitParticlesDelegate emitParticles;
    private static int emitterResolutionAttempted;

    private delegate void EmitParticlesDelegate(
        TileDrawing drawing,
        int tileY,
        int tileX,
        Tile tile,
        ushort type,
        short frameX,
        short frameY,
        Color light);

    internal static bool TryDraw(
        TileDrawing drawing,
        bool solidLayer,
        Vector2 screenPosition,
        Vector2 screenOffset,
        int tileX,
        int tileY)
    {
        if (drawing == null || !solidLayer || !CanUseStaticChunks())
        {
            return false;
        }

        Tile[,] tiles = Main.tile;
        if (tiles == null || tileX <= 0 || tileY <= 0 || tileX >= Main.maxTilesX - 1 || tileY >= Main.maxTilesY - 1)
        {
            return false;
        }

        ResetForWorldChange(tiles);
        Tile tile = tiles[tileX, tileY];
        if (!IsCacheable(tile, tileX, tileY))
        {
            return false;
        }

        // DrawSingleTile can emit dust and advance its deterministic tile effect sequence.
        // If the version-locked delegate is unavailable, retaining the native call is the only
        // behavior-preserving choice for those tiles.
        EmitParticlesDelegate particleEmitter = null;
        if (TileID.Sets.MakesRubbleDust[tile.type])
        {
            particleEmitter = ResolveEmitter();
            if (particleEmitter == null)
            {
                return false;
            }
        }

        int chunkX = tileX / ChunkTileSize;
        int chunkY = tileY / ChunkTileSize;
        ChunkSlot slot = Slots[(chunkX & ChunkSlotMaskX) + ((chunkY & ChunkSlotMaskY) * ChunkSlotWidth)];
        if (!slot.Matches(chunkX, chunkY) || slot.Dirty)
        {
            slot.Prepare(chunkX, chunkY);
        }

        int localX = tileX - chunkX * ChunkTileSize;
        int localY = tileY - chunkY * ChunkTileSize;
        ref Descriptor descriptor = ref slot.Descriptors[localX + localY * ChunkTileSize];
        if (!descriptor.Matches(tile, slot.Revision))
        {
            PopulateDescriptor(ref descriptor, slot.Revision, tile, tileX, tileY, drawing);
        }

        if (!descriptor.IsStatic || !IsCacheable(tile, tileX, tileY))
        {
            return false;
        }

        Color light = Lighting.GetColor(tileX, tileY);

        // The native renderer emits a separate black tile when there is no light. Drawing a
        // transparent descriptor instead leaves small seams between otherwise dark blocks.
        if ((light.R | light.G | light.B) == 0)
        {
            return false;
        }

        DrawLiveLitDescriptor(descriptor, tileX, tileY, light, screenPosition, screenOffset);
        EmitNativeParticles(drawing, tileX, tileY, tile, light, particleEmitter);
        return true;
    }

    /// <summary>
    /// Called by the version-locked mutation patch. The descriptor comparison remains a final
    /// safeguard for direct writes performed by world generation or older networking paths.
    /// </summary>
    internal static void Invalidate(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0)
        {
            return;
        }

        int chunkX = tileX / ChunkTileSize;
        int chunkY = tileY / ChunkTileSize;
        for (int y = chunkY - 1; y <= chunkY + 1; y++)
        {
            for (int x = chunkX - 1; x <= chunkX + 1; x++)
            {
                if (x < 0 || y < 0)
                {
                    continue;
                }

                ChunkSlot slot = Slots[(x & ChunkSlotMaskX) + ((y & ChunkSlotMaskY) * ChunkSlotWidth)];
                if (slot.Matches(x, y))
                {
                    slot.Dirty = true;
                }
            }
        }
    }

    internal static void InvalidateAll()
    {
        for (int index = 0; index < Slots.Length; index++)
        {
            Slots[index].Clear();
        }
    }

    /// <summary>Resolves the version-locked native dust emitter away from the first visible tile draw.</summary>
    internal static void Prewarm()
    {
        if (emitterResolutionAttempted == 0)
        {
            ResolveEmitter();
        }
    }

    private static bool CanUseStaticChunks()
    {
        if (Main.gameMenu || Main.shimmerAlpha > 0f || DebugOptions.devLightTilesCheat)
        {
            return false;
        }

        Player player = Main.LocalPlayer;
        return player != null && !player.dangerSense && !player.findTreasure && !player.biomeSight;
    }

    private static void ResetForWorldChange(Tile[,] tiles)
    {
        if (ReferenceEquals(observedTileMap, tiles))
        {
            return;
        }

        observedTileMap = tiles;
        InvalidateAll();
    }

    /// <summary>
    /// Fills only the descriptor actually requested by Terraria.  Earlier revisions eagerly
    /// inspected all 400 tiles on first chunk contact, causing a visible main-thread burst while
    /// the native loop was already walking those same tiles.  Per-descriptor revisions preserve
    /// invalidation correctness while naturally warming a chunk across its normal draw traversal.
    /// </summary>
    private static void PopulateDescriptor(ref Descriptor descriptor, int revision, Tile tile, int tileX, int tileY, TileDrawing drawing)
    {
        descriptor.Clear();
        descriptor.Revision = revision;
        if (!IsCacheable(tile, tileX, tileY))
        {
            return;
        }

        descriptor.Set(tile, drawing.GetTileDrawTexture(tile, tileX, tileY));
    }

    private static bool IsCacheable(Tile tile, int tileX, int tileY)
    {
        if (tile == null || !tile.active() || tile.liquid != 0 || tile.color() != 0 ||
            tile.inActive() || tile.fullbrightBlock() || tile.invisibleBlock() || tile.halfBrick() || tile.slope() != 0)
        {
            return false;
        }

        ushort type = tile.type;
        if (!StaticTileChunkEligibility.IsEligible(type) ||
            (tile.wall != 0 && (tile.wall == 318 || tile.fullbrightWall())))
        {
            return false;
        }

        Tile left = Main.tile[tileX - 1, tileY];
        Tile right = Main.tile[tileX + 1, tileY];
        return left == null || right == null || (!left.halfBrick() && !right.halfBrick());
    }

    private static void DrawLiveLitDescriptor(
        Descriptor descriptor,
        int tileX,
        int tileY,
        Color light,
        Vector2 screenPosition,
        Vector2 screenOffset)
    {
        Vector2 position = new Vector2(tileX * 16 - (int)screenPosition.X, tileY * 16 - (int)screenPosition.Y) + screenOffset;
        Rectangle source = new Rectangle(descriptor.FrameX, descriptor.FrameY, 16, 16);
        GetNativeLightingThresholds(out Color highQualityThreshold, out Color mediumQualityThreshold);
        if (light.R > highQualityThreshold.R || light.G > highQualityThreshold.G || light.B > highQualityThreshold.B)
        {
            Lighting.GetColor9Slice(tileX, tileY, ref NineLightSlices);
            DrawSlices(descriptor.Texture, position, source, light.ToVector3(), NineLightSlices, 9);
            return;
        }

        if (light.R > mediumQualityThreshold.R || light.G > mediumQualityThreshold.G || light.B > mediumQualityThreshold.B)
        {
            Lighting.GetColor4Slice(tileX, tileY, ref FourLightSlices);
            DrawSlices(descriptor.Texture, position, source, light.ToVector3(), FourLightSlices, 4);
            return;
        }

        Main.tileBatch.Draw(descriptor.Texture, position, source, light, Vector2.Zero, 1f, SpriteEffects.None);
    }

    // Mirrors TileDrawing.Draw exactly: the thresholds are byte fields, including the native
    // unchecked byte conversion at low graphics quality. Comparing floats here chose the wrong
    // slice resolution and visibly changed Terraria's lighting gradients.
    private static void GetNativeLightingThresholds(out Color highQualityThreshold, out Color mediumQualityThreshold)
    {
        float highQualityBase = 255f * (1f - Main.gfxQuality) + 30f * Main.gfxQuality;
        highQualityThreshold = new Color(
            unchecked((byte)highQualityBase),
            unchecked((byte)(highQualityBase * 1.1f)),
            unchecked((byte)(highQualityBase * 1.2f)));

        float mediumQualityBase = 50f * (1f - Main.gfxQuality) + 2f * Main.gfxQuality;
        mediumQualityThreshold = new Color(
            unchecked((byte)mediumQualityBase),
            unchecked((byte)(mediumQualityBase * 1.1f)),
            unchecked((byte)(mediumQualityBase * 1.2f)));

        if (DebugOptions.devLightTilesCheat)
        {
            highQualityThreshold = Color.White;
            mediumQualityThreshold = Color.White;
        }
    }

    private static void DrawSlices(Texture2D texture, Vector2 position, Rectangle source, Vector3 center, Vector3[] slices, int count)
    {
        if (count == 9)
        {
            for (int index = 0; index < 9; index++)
            {
                int x = 0;
                int y = 0;
                int width = 4;
                int height = 4;
                switch (index)
                {
                    case 1: x = 4; width = 8; break;
                    case 2: x = 12; break;
                    case 3: y = 4; height = 8; break;
                    case 4: x = 4; y = 4; width = 8; height = 8; break;
                    case 5: x = 12; y = 4; height = 8; break;
                    case 6: y = 12; break;
                    case 7: x = 4; y = 12; width = 8; break;
                    case 8: x = 12; y = 12; break;
                }

                Main.tileBatch.Draw(texture, position + new Vector2(x, y), new Rectangle(source.X + x, source.Y + y, width, height), ToNativeSliceColor(slices[index], center), Vector2.Zero, 1f, SpriteEffects.None);
            }

            return;
        }

        for (int index = 0; index < 4; index++)
        {
            int x = (index & 1) * 8;
            int y = (index >> 1) * 8;
            Main.tileBatch.Draw(texture, position + new Vector2(x, y), new Rectangle(source.X + x, source.Y + y, 8, 8), ToNativeSliceColor(slices[index], center), Vector2.Zero, 1f, SpriteEffects.None);
        }
    }

    private static Color ToNativeSliceColor(Vector3 slice, Vector3 center)
    {
        int red = (int)(((slice.X + center.X) * 0.5f) * 255f);
        int green = (int)(((slice.Y + center.Y) * 0.5f) * 255f);
        int blue = (int)(((slice.Z + center.Z) * 0.5f) * 255f);
        if (red > 255)
        {
            red = 255;
        }

        if (green > 255)
        {
            green = 255;
        }

        if (blue > 255)
        {
            blue = 255;
        }

        Color color = default;
        color.PackedValue = (uint)(red | green << 8 | blue << 16 | -16777216);
        return color;
    }

    private static void EmitNativeParticles(
        TileDrawing drawing,
        int tileX,
        int tileY,
        Tile tile,
        Color light,
        EmitParticlesDelegate particleEmitter)
    {
        if (!TileID.Sets.MakesRubbleDust[tile.type] || !FocusHelper.AllowTileDrawingToEmitEffects ||
            (Lighting.UpdateEveryFrame && new FastRandom(Main.TileFrameSeed).WithModifier(tileX, tileY).Next(4) != 0))
        {
            return;
        }

        particleEmitter(drawing, tileY, tileX, tile, tile.type, tile.frameX, tile.frameY, light);
    }

    private static EmitParticlesDelegate ResolveEmitter()
    {
        if (emitterResolutionAttempted != 0)
        {
            return emitParticles;
        }

        lock (EmitterResolutionGate)
        {
            if (emitterResolutionAttempted != 0)
            {
                return emitParticles;
            }

            try
            {
                MethodInfo method = typeof(TileDrawing).GetMethod(
                    "DrawTiles_EmitParticles",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int), typeof(int), typeof(Tile), typeof(ushort), typeof(short), typeof(short), typeof(Color) },
                    null);
                if (method != null)
                {
                    emitParticles = (EmitParticlesDelegate)Delegate.CreateDelegate(typeof(EmitParticlesDelegate), null, method);
                }
            }
            catch
            {
                emitParticles = null;
            }

            emitterResolutionAttempted = 1;
            return emitParticles;
        }
    }

    private static ChunkSlot[] CreateSlots()
    {
        var slots = new ChunkSlot[ChunkSlotWidth * ChunkSlotHeight];
        for (int index = 0; index < slots.Length; index++)
        {
            slots[index] = new ChunkSlot();
        }

        return slots;
    }

    private sealed class ChunkSlot
    {
        internal readonly Descriptor[] Descriptors = new Descriptor[TilesPerChunk];
        internal int ChunkX = int.MinValue;
        internal int ChunkY = int.MinValue;
        internal bool Dirty = true;
        internal int Revision;

        internal bool Matches(int chunkX, int chunkY)
        {
            return ChunkX == chunkX && ChunkY == chunkY;
        }

        internal void Clear()
        {
            ChunkX = int.MinValue;
            ChunkY = int.MinValue;
            Dirty = true;
            unchecked
            {
                Revision++;
            }
        }

        internal void Prepare(int chunkX, int chunkY)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            Dirty = false;
            unchecked
            {
                Revision++;
            }
        }
    }

    private struct Descriptor
    {
        internal ushort Type;
        internal short FrameX;
        internal short FrameY;
        internal Texture2D Texture;
        internal bool IsStatic;
        internal int Revision;

        internal void Set(Tile tile, Texture2D texture)
        {
            Type = tile.type;
            FrameX = tile.frameX;
            FrameY = tile.frameY;
            Texture = texture;
            IsStatic = texture != null;
        }

        internal bool Matches(Tile tile, int revision)
        {
            return Revision == revision && IsStatic && tile != null && tile.active() && tile.type == Type && tile.frameX == FrameX && tile.frameY == FrameY;
        }

        internal void Clear()
        {
            Type = 0;
            FrameX = 0;
            FrameY = 0;
            Texture = null;
            IsStatic = false;
        }
    }
}
