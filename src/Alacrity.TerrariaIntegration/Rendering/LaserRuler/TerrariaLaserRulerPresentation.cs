using System;
using System.Reflection;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace AlacrityTerraria.Rendering.LaserRuler;

/// <summary>
/// Version-locked batched presentation for the mechanical laser ruler. The native texture is
/// deliberately reused: its lower half contains the ordinary grid and its upper half contains
/// the highlighted grid. Reverse gravity and unusual cursor bounds retain Terraria's native
/// per-cell renderer because its source-rectangle placement is the authoritative behavior.
/// </summary>
internal static class TerrariaLaserRulerPresentation
{
    private const int CellSize = 16;
    private const float InteriorTextureFactor = 127f / 255f;

    private static readonly Rectangle OrdinaryInteriorSource = new Rectangle(2, 20, 14, 14);
    private static readonly Rectangle OrdinaryVerticalSource = new Rectangle(0, 18, 2, 16);
    private static readonly Rectangle OrdinaryHorizontalSource = new Rectangle(0, 18, 16, 2);
    private static readonly Rectangle HighlightInteriorSource = new Rectangle(2, 2, 14, 14);
    private static readonly Rectangle HighlightVerticalSource = new Rectangle(0, 0, 2, 16);
    private static readonly Rectangle HighlightHorizontalSource = new Rectangle(0, 0, 16, 2);
    private static readonly object TextureGate = new object();
    private static Func<Texture2D> extraTextureGetter;
    private static int textureResolutionAttempted;

    /// <summary>
    /// Draws the normal-gravity ruler with one translucent fill, one line strip per visible grid
    /// axis, and a compact highlighted cross. Returning false never mutates SpriteBatch and lets
    /// the caller continue through the unmodified Terraria implementation.
    /// </summary>
    internal static bool TryDraw()
    {
        int playerIndex = Main.myPlayer;
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
        {
            return false;
        }

        Player localPlayer = Main.player[playerIndex];
        if (localPlayer == null ||
            !localPlayer.rulerGrid ||
            localPlayer.builderAccStatus[1] != 0 ||
            localPlayer.gravDir == -1f)
        {
            return false;
        }

        int columns = (Main.screenWidth + 100) / CellSize;
        int rows = (Main.screenHeight + 100) / CellSize;
        if (columns <= 0 || rows <= 0 || Main.spriteBatch == null)
        {
            return false;
        }

        int gridTileX = (int)Math.Floor((Main.screenPosition.X - 50f) / CellSize);
        int gridTileY = (int)Math.Floor((Main.screenPosition.Y - 50f) / CellSize);
        Vector2 gridWorldOrigin = new Vector2(gridTileX * CellSize, gridTileY * CellSize);
        Vector2 mouseWorld = Main.MouseWorld;
        Point hoveredCell = new Point(
            (int)Math.Floor(mouseWorld.X / CellSize) - gridTileX,
            (int)Math.Floor(mouseWorld.Y / CellSize) - gridTileY);
        if (hoveredCell.X < 0 || hoveredCell.X >= columns || hoveredCell.Y < 0 || hoveredCell.Y >= rows)
        {
            return false;
        }

        Texture2D texture = GetTexture();
        if (texture == null)
        {
            return false;
        }

        float motion = Vector2.Distance(localPlayer.position, localPlayer.shadowPos[2]);
        float intensity = MathHelper.Lerp(0.2f, 0.7f, MathHelper.Clamp(1f - motion / 6f, 0f, 1f));
        Color ordinaryColor = new Color(0.24f, 0.8f, 0.9f, 0.5f) * 0.4f * intensity;
        Color highlightColor = new Color(1f, 0.8f, 0.9f, 0.5f) * 0.5f * intensity;
        Vector2 origin = gridWorldOrigin - Main.screenPosition - Vector2.One;
        float width = columns * CellSize;
        float height = rows * CellSize;

        DrawOrdinaryGrid(Main.spriteBatch, texture, origin, columns, rows, width, height, ordinaryColor);
        DrawHighlight(Main.spriteBatch, texture, origin, hoveredCell, width, height, highlightColor);
        return true;
    }

    private static void DrawOrdinaryGrid(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Vector2 origin,
        int columns,
        int rows,
        float width,
        float height,
        Color ordinaryColor)
    {
        // The source interior is uniformly 127/255 grey/alpha. The adjusted edge tint accounts
        // for the large interior fill already beneath a line, matching native alpha composition
        // without submitting one 16x16 sprite for every visible cell.
        spriteBatch.Draw(
            texture,
            origin,
            OrdinaryInteriorSource,
            ordinaryColor,
            0f,
            Vector2.Zero,
            new Vector2(width / OrdinaryInteriorSource.Width, height / OrdinaryInteriorSource.Height),
            SpriteEffects.None,
            0f);

        Color edgeColor = CompensateForInterior(ordinaryColor);
        for (int column = 0; column < columns; column++)
        {
            spriteBatch.Draw(
                texture,
                new Vector2(origin.X + column * CellSize, origin.Y),
                OrdinaryVerticalSource,
                edgeColor,
                0f,
                Vector2.Zero,
                new Vector2(1f, height / OrdinaryVerticalSource.Height),
                SpriteEffects.None,
                0f);
        }

        for (int row = 0; row < rows; row++)
        {
            spriteBatch.Draw(
                texture,
                new Vector2(origin.X, origin.Y + row * CellSize),
                OrdinaryHorizontalSource,
                edgeColor,
                0f,
                Vector2.Zero,
                new Vector2(width / OrdinaryHorizontalSource.Width, 1f),
                SpriteEffects.None,
                0f);
        }
    }

    private static void DrawHighlight(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Vector2 origin,
        Point hoveredCell,
        float width,
        float height,
        Color highlightColor)
    {
        float columnX = origin.X + hoveredCell.X * CellSize;
        float rowY = origin.Y + hoveredCell.Y * CellSize;
        Color edgeColor = CompensateForInterior(highlightColor);

        spriteBatch.Draw(
            texture,
            new Vector2(columnX, origin.Y),
            HighlightInteriorSource,
            highlightColor,
            0f,
            Vector2.Zero,
            new Vector2(CellSize / (float)HighlightInteriorSource.Width, height / HighlightInteriorSource.Height),
            SpriteEffects.None,
            0f);

        if (hoveredCell.X > 0)
        {
            spriteBatch.Draw(
                texture,
                new Vector2(origin.X, rowY),
                HighlightInteriorSource,
                highlightColor,
                0f,
                Vector2.Zero,
                new Vector2((hoveredCell.X * CellSize) / (float)HighlightInteriorSource.Width, CellSize / (float)HighlightInteriorSource.Height),
                SpriteEffects.None,
                0f);
        }

        float rightWidth = width - (hoveredCell.X + 1) * CellSize;
        if (rightWidth > 0f)
        {
            spriteBatch.Draw(
                texture,
                new Vector2(columnX + CellSize, rowY),
                HighlightInteriorSource,
                highlightColor,
                0f,
                Vector2.Zero,
                new Vector2(rightWidth / HighlightInteriorSource.Width, CellSize / (float)HighlightInteriorSource.Height),
                SpriteEffects.None,
                0f);
        }

        spriteBatch.Draw(
            texture,
            new Vector2(columnX, origin.Y),
            HighlightVerticalSource,
            edgeColor,
            0f,
            Vector2.Zero,
            new Vector2(1f, height / HighlightVerticalSource.Height),
            SpriteEffects.None,
            0f);
        spriteBatch.Draw(
            texture,
            new Vector2(columnX + CellSize, origin.Y),
            HighlightVerticalSource,
            edgeColor,
            0f,
            Vector2.Zero,
            new Vector2(1f, height / HighlightVerticalSource.Height),
            SpriteEffects.None,
            0f);
        spriteBatch.Draw(
            texture,
            new Vector2(origin.X, rowY),
            HighlightHorizontalSource,
            edgeColor,
            0f,
            Vector2.Zero,
            new Vector2(width / HighlightHorizontalSource.Width, 1f),
            SpriteEffects.None,
            0f);
        spriteBatch.Draw(
            texture,
            new Vector2(origin.X, rowY + CellSize),
            HighlightHorizontalSource,
            edgeColor,
            0f,
            Vector2.Zero,
            new Vector2(width / HighlightHorizontalSource.Width, 1f),
            SpriteEffects.None,
            0f);
    }

    private static Color CompensateForInterior(Color color)
    {
        float alpha = color.A / 255f;
        float multiplier = (1f - InteriorTextureFactor) / (1f - alpha * InteriorTextureFactor);
        return color * multiplier;
    }

    private static Texture2D GetTexture()
    {
        Func<Texture2D> getter = Volatile.Read(ref extraTextureGetter);
        if (getter != null)
        {
            return getter();
        }

        if (Volatile.Read(ref textureResolutionAttempted) != 0)
        {
            return null;
        }

        lock (TextureGate)
        {
            getter = extraTextureGetter;
            if (getter == null && textureResolutionAttempted == 0)
            {
                try
                {
                    FieldInfo extraField = typeof(TextureAssets).GetField("Extra", BindingFlags.Public | BindingFlags.Static);
                    Array extraAssets = extraField?.GetValue(null) as Array;
                    object asset = extraAssets?.GetValue(68);
                    MethodInfo valueGetter = asset?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetMethod;
                    if (valueGetter != null)
                    {
                        getter = (Func<Texture2D>)Delegate.CreateDelegate(typeof(Func<Texture2D>), valueGetter);
                        Volatile.Write(ref extraTextureGetter, getter);
                    }
                }
                catch
                {
                    // A missing/stale asset surface is an optional-renderer failure. The caller
                    // immediately continues through Terraria's untouched laser-ruler path.
                }
                finally
                {
                    Volatile.Write(ref textureResolutionAttempted, 1);
                }
            }
        }

        getter = Volatile.Read(ref extraTextureGetter);
        return getter == null ? null : getter();
    }
}
