# Static Tile Chunk Presentation

## Scope

Kinesin 0.7.0 adds **Optimize Static Tile Chunks**, enabled by default. The setting requests the
generic `PluginRenderingOptimization.StaticTileChunkPresentation` category through
`context.Terraria.RenderingOptimizations`. Kinesin remains an ordinary PluginSdk consumer and
does not receive Terraria tiles, textures, or graphics objects.

## Vanilla behavior

Terraria 1.4.5.6 visits every visible tile through `TileDrawing.Draw` and resolves its texture,
draw data, light slices, visibility effects, particles, and draw command each frame. That path is
authoritative for animated, painted, slope, half-block, liquid, glowing, outlined, coated,
visibility-sensitive, and special tiles.

## Alacrity method

The host owns fixed 20-by-20 tile descriptor regions. `tools/StaticTileChunkAudit` parses the
version-locked decompiled Terraria metadata and emits an immutable bitset allow table in
`StaticTileChunkEligibility.Generated.cs`. A candidate must be a full solid brick and is rejected
when Terraria identifies it as frame-important, glowing, shiny, flaming, wind-driven, foliage, a
vine, beam, falling block, cloud, mechanism, trigger, outline, platform, special wall drawer, or
another known dynamic/special renderer. Per-tile state still rejects paint, coatings, liquids,
slopes, half blocks, actuation, visibility effects, and unsafe neighboring geometry. A region
retains immutable texture/frame descriptors;
live Terraria lighting is still sampled and rendered every frame, including the native one-, four-,
and nine-slice lighting paths. Rendering is submitted through Terraria's existing `TileBatch`, so
the normal texture batching and layer behavior remain authoritative.

The cache is invalidated from version-locked tile, wall, paint, framing, actuation, and network
mutation entry points. Descriptor comparison remains a final safeguard for direct writes and
world-generation paths. Neighboring 20-by-20 regions are invalidated because a tile can affect a
boundary tile's native draw eligibility.

This first conservative cache does not bake light into a render target and does not attempt to
cache walls, foliage, vines, entities, special points, or dynamic tile effects. A permanently lit
chunk texture would visibly stale under Terraria's dynamic lighting. A full shader-backed static
tile-and-wall renderer needs a reproducible light-map shader build and a complete replacement for
the native special-tile path; that larger design is intentionally not presented as completed here.

## Regenerating the eligibility table

The generated file is checked in so a normal client build never needs decompiled source or runs an
audit at startup. After changing the version-locked audit rules or upgrading Terraria, regenerate
the table explicitly:

```powershell
dotnet run --project tools/StaticTileChunkAudit/Alacrity.StaticTileChunkAudit.csproj -c Release -- "Decompiled Terraria 1.4.5.6" "src/Alacrity.TerrariaIntegration/Rendering/TileChunks/StaticTileChunkEligibility.Generated.cs"
```

## Fallback and safety

The patched `TileDrawing.Draw` call first asks the host to render only an audited descriptor. Any
unsupported tile, uncertainty about native particle emission, unusual visibility state, or missing
asset immediately returns to Terraria's untouched `DrawSingleTile` path. The optimization is local
presentation only and does not change world state, networking, RNG order, or gameplay.
