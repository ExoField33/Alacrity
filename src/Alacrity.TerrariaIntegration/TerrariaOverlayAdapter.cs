using System;
using System.Diagnostics;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria;

/// <summary>
/// Owns the Terraria-side overlay draw boundary, including graphics resources and frame metadata.
/// The bridge facade supplies phase gating and exception reporting; plugins only receive SDK canvas calls.
/// </summary>
internal sealed class TerrariaOverlayAdapter : IDisposable
{
    private readonly Stopwatch presentationClock = Stopwatch.StartNew();
    private readonly TerrariaOverlayGraphicsResources graphicsResources = new TerrariaOverlayGraphicsResources();
    private readonly TerrariaOverlayCanvas canvas;

    internal TerrariaOverlayAdapter()
    {
        canvas = new TerrariaOverlayCanvas(graphicsResources);
    }

    internal void Dispatch(SpriteBatch spriteBatch, PluginOverlayHost overlays, PluginOverlaySpace space)
    {
        if (spriteBatch == null) throw new ArgumentNullException(nameof(spriteBatch));
        if (overlays == null) throw new ArgumentNullException(nameof(overlays));

        graphicsResources.Prepare(spriteBatch.GraphicsDevice);
        canvas.Begin(spriteBatch, space);
        overlays.Dispatch(canvas, new PluginOverlayFrame(
            Main.screenWidth,
            Main.screenHeight,
            Main.UIScale,
            Main.gameMenu,
            presentationClock.Elapsed,
            Main.GameUpdateCount), space);
    }

    public void Dispose()
    {
        graphicsResources.Dispose();
    }
}
