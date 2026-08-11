# Tile Drawing Presentation

Kinesin 0.4.0 adds a default-enabled **Optimize Tile Drawing** policy for Terraria 1.4.5.6. The
policy is requested through `IPluginRenderingOptimizationService`; the plugin does not receive
Terraria tiles, textures, paint systems, or SpriteBatch objects.

## Vanilla work

`TileDrawing.GetTileDrawData` obtains `Lighting.GetColor(x, y)` for every tile that reaches the
method. In the verified 1.4.5.6 implementation, that local is only consumed by glow-tile cases
637 and 638. Ordinary tiles still pay for the lighting lookup even though the resulting color is
discarded.

`DrawLiquidBehindTiles` also calls `TileBatch.SetLayer(0, 0)` once for every visited tile. The
dedicated liquid-behind helper does not change the selected layer, so after the first call the
remaining identical selections do no useful work.

## Alacrity path

The version-locked patch captures Kinesin's policy once at `TileDrawing.Draw` entry. While it is
active, the glow-light helper calls native `Lighting.GetColor` only for tile types 637 and 638;
all other types receive a default color that remains unobserved by Terraria's original switch.

For the standalone liquid-behind pass, Alacrity resets a per-pass marker, performs the first
native `SetLayer`, and skips only later identical calls in that same pass. If the policy is off,
every native call still runs.

Paint lookup, texture loading, trees, palms, vines, wind, liquid routing, special tiles, random
effects, entity discovery, draw order, and lighting for visible output all remain native. A
generic direct `Asset<T>` texture shortcut was investigated and intentionally rejected because
the target executable's generic metadata makes that route unsuitable for a safe injected fast
path.

## Expected impact

The lighting reduction applies to many visible tiles every frame, while the layer reduction scales
with the visible liquid-behind scan area. Both paths are allocation-free after patch application.
The exact frame-time gain depends on screen resolution, lighting mode, and terrain; these changes
target steady per-tile CPU work rather than altering visual fidelity.
