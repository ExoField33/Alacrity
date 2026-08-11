# Laser Ruler Presentation

## Scope

Kinesin 0.6.0 adds **Optimize Laser Ruler Rendering**, enabled by default. The setting requests
the generic `PluginRenderingOptimization.LaserRulerPresentation` category through
`context.Terraria.RenderingOptimizations`. Kinesin never receives Terraria input, player state,
textures, or SpriteBatch access.

## Vanilla behavior

Terraria 1.4.5.6's `Main.DrawInterface_3_LaserRuler` draws the ordinary part of the ruler as one
18-by-18 texture sprite for nearly every visible grid cell, then draws the highlighted row and
column separately. At desktop resolutions this can be several thousand SpriteBatch submissions
for a single UI overlay. The native method derives its grid origin from the camera, fades from the
local player's movement, and uses `TextureAssets.Extra[68]` for both normal and selected cells.

## Alacrity method

When the generic policy is active, the version-locked bridge first offers the draw to a
TerrariaIntegration-owned renderer. It uses the same native texture, camera-origin calculation,
fade calculation, colors, cell size, and mouse-cell selection. A stretched interior rectangle
provides the normal grid background; one strip is submitted for each visible vertical and
horizontal line; compact selected-row and selected-column strips replace the remaining selected
cells. No SpriteBatch state is changed and no per-frame managed allocation is introduced.

The normal-gravity path therefore uses approximately one background draw, one draw per visible
vertical line, one per visible horizontal line, and a small fixed selected-cross set. A 1920 by
1080 viewport is normally about 190 to 210 submissions instead of roughly 8,000 to 10,000 normal
cell submissions plus the selection strips. The exact count depends on resolution.

## Fallback and safety

The optimized renderer returns `false` without touching SpriteBatch when the native state cannot
be reproduced safely, including reverse gravity, an unavailable native texture, or an unexpected
cursor position outside the prepared grid. The injected method then runs Terraria's untouched
implementation. This keeps reverse-gravity orientation and version-sensitive edge cases
authoritative in vanilla code.

The bridge patch only changes local presentation. It does not affect ruler input, cursor state,
player movement, tile placement, game logic, network state, or random-number ordering.
