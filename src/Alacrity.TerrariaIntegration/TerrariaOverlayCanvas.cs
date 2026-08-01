using System;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria;

/// <summary>Terraria-owned implementation of the SDK overlay canvas; no raw renderer escapes to plugins.</summary>
internal sealed class TerrariaOverlayCanvas : IPluginOverlayCanvas
{
    private SpriteBatch spriteBatch;
    private readonly TerrariaOverlayGraphicsResources resources;

    internal TerrariaOverlayCanvas(TerrariaOverlayGraphicsResources resources)
    {
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    internal void Begin(SpriteBatch spriteBatch)
    {
        this.spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
    }

    public void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f)
    {
        if (!string.IsNullOrEmpty(text) && scale > 0f)
            Utils.DrawBorderString(spriteBatch, text, new Vector2(x, y), ToColor(color), scale);
    }

    public void FillRectangle(float x, float y, float width, float height, PluginOverlayColor color)
    {
        if (width <= 0f || height <= 0f || !resources.TryGetPixel(out Texture2D texture)) return;
        spriteBatch.Draw(texture, new Rectangle((int)x, (int)y, (int)width, (int)height), ToColor(color));
    }

    public void DrawRectangle(float x, float y, float width, float height, PluginOverlayColor color, float thickness = 1f)
    {
        if (width <= 0f || height <= 0f || thickness <= 0f) return;
        float edge = Math.Min(thickness, Math.Min(width, height) * 0.5f);
        FillRectangle(x, y, width, edge, color);
        FillRectangle(x, y + height - edge, width, edge, color);
        FillRectangle(x, y, edge, height, color);
        FillRectangle(x + width - edge, y, edge, height, color);
    }

    public void DrawLine(float startX, float startY, float endX, float endY, PluginOverlayColor color, float thickness = 1f)
    {
        if (thickness <= 0f || !resources.TryGetPixel(out Texture2D texture)) return;
        Vector2 start = new Vector2(startX, startY);
        Vector2 delta = new Vector2(endX - startX, endY - startY);
        spriteBatch.Draw(texture, start, null, ToColor(color), (float)System.Math.Atan2(delta.Y, delta.X), Vector2.Zero, new Vector2(delta.Length(), thickness), SpriteEffects.None, 0f);
    }

    public void DrawAsset(string approvedAssetId, float x, float y, float scale = 1f, PluginOverlayColor? tint = null)
    {
        if (approvedAssetId == "ui:pixel") FillRectangle(x, y, scale, scale, tint ?? new PluginOverlayColor(255, 255, 255));
    }

    public void DrawWorldMarker(float worldX, float worldY, string text, PluginOverlayColor color)
    {
        Vector2 position = Project(worldX, worldY);
        DrawText(text, position.X, position.Y, color);
    }

    public void DrawWorldRectangle(float worldX, float worldY, float width, float height, PluginOverlayColor color, float thickness = 1f)
    {
        Vector2 topLeft = Project(worldX, worldY);
        Vector2 bottomRight = Project(worldX + width, worldY + height);
        DrawRectangle(System.Math.Min(topLeft.X, bottomRight.X), System.Math.Min(topLeft.Y, bottomRight.Y), System.Math.Abs(bottomRight.X - topLeft.X), System.Math.Abs(bottomRight.Y - topLeft.Y), color, thickness);
    }

    private static Vector2 Project(float worldX, float worldY)
    {
        // The verified world-overlay hook runs in Terraria's already screen-space SpriteBatch pass.
        // Applying GameViewMatrix or UIScale here would transform the coordinates a second time.
        return new Vector2(worldX, worldY) - Main.screenPosition;
    }

    private static Color ToColor(PluginOverlayColor color) => new Color(color.Red, color.Green, color.Blue, color.Alpha);
}

/// <summary>Owns integration-only graphics resources and prepares them before plugin callbacks run.</summary>
internal sealed class TerrariaOverlayGraphicsResources : IDisposable
{
    private Texture2D pixel;

    internal void Prepare(GraphicsDevice graphicsDevice)
    {
        if (graphicsDevice == null || (pixel != null && !pixel.IsDisposed)) return;
        try
        {
            var created = new Texture2D(graphicsDevice, 1, 1);
            created.SetData(new[] { Color.White });
            pixel = created;
        }
        catch
        {
            pixel = null;
        }
    }

    internal bool TryGetPixel(out Texture2D texture)
    {
        texture = pixel;
        return texture != null && !texture.IsDisposed;
    }

    public void Dispose()
    {
        if (pixel == null) return;
        pixel.Dispose();
        pixel = null;
    }
}
