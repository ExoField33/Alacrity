using System;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria;

/// <summary>Terraria-owned implementation of the SDK overlay canvas; no raw renderer escapes to plugins.</summary>
internal sealed class TerrariaOverlayCanvas : IPluginOverlayCanvas
{
    private readonly SpriteBatch spriteBatch;
    private static Texture2D pixel;

    internal TerrariaOverlayCanvas(SpriteBatch spriteBatch) => this.spriteBatch = spriteBatch;

    public void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f)
    {
        if (!string.IsNullOrEmpty(text) && scale > 0f)
            Utils.DrawBorderString(spriteBatch, text, new Vector2(x, y), ToColor(color), scale);
    }

    public void FillRectangle(float x, float y, float width, float height, PluginOverlayColor color)
    {
        if (width <= 0f || height <= 0f || !EnsurePixel()) return;
        spriteBatch.Draw(pixel, new Rectangle((int)x, (int)y, (int)width, (int)height), ToColor(color));
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
        if (thickness <= 0f || !EnsurePixel()) return;
        Vector2 start = new Vector2(startX, startY);
        Vector2 delta = new Vector2(endX - startX, endY - startY);
        spriteBatch.Draw(pixel, start, null, ToColor(color), (float)System.Math.Atan2(delta.Y, delta.X), Vector2.Zero, new Vector2(delta.Length(), thickness), SpriteEffects.None, 0f);
    }

    public void DrawAsset(string approvedAssetId, float x, float y, float scale = 1f, PluginOverlayColor? tint = null)
    {
        if (approvedAssetId == "ui:pixel") FillRectangle(x, y, scale, scale, tint ?? new PluginOverlayColor(255, 255, 255));
    }

    public void DrawWorldMarker(float worldX, float worldY, string text, PluginOverlayColor color)
    {
        DrawText(text, worldX - Main.screenPosition.X, worldY - Main.screenPosition.Y, color);
    }

    public void DrawWorldRectangle(float worldX, float worldY, float width, float height, PluginOverlayColor color, float thickness = 1f)
    {
        DrawRectangle(worldX - Main.screenPosition.X, worldY - Main.screenPosition.Y, width, height, color, thickness);
    }

    private bool EnsurePixel()
    {
        if (pixel != null && !pixel.IsDisposed) return true;
        try { pixel = new Texture2D(spriteBatch.GraphicsDevice, 1, 1); pixel.SetData(new[] { Color.White }); return true; }
        catch { pixel = null; return false; }
    }

    private static Color ToColor(PluginOverlayColor color) => new Color(color.Red, color.Green, color.Blue, color.Alpha);
}
