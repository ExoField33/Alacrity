# Painted Tile Preparation

## Scope

Kinesin is a trusted local-performance plugin. Its first setting, **Optimize Painted Tile
Preparation**, is enabled by default and requests the generic
`context.Terraria.RenderingOptimizations` `PaintedTilePreparation` policy. The setting can be
disabled to leave the patched client on its unoptimized vanilla queue behavior.

This is intentionally not a public low-level renderer API. Ordinary plugins continue to use
framework-neutral PluginSdk services and cannot access `TilePaintSystemV2`, `RenderTarget2D`,
`SpriteBatch`, or mutable Terraria tiles.

## Vanilla behavior

During `TileDrawing.PrepareForAreaDrawing`, Terraria discovers painted tile, wall, and foliage
variants and calls the relevant `TilePaintSystemV2.Request*` method. Vanilla caches holders by
variation key, but every request made before `PrepareAllRequests` can append the same unready
holder to `_requests` again. A dense area containing thousands of instances of one paint variant
can therefore make the synchronous preparation loop traverse and invoke `Prepare()` thousands of
times for one holder.

The render target and shader work itself remains synchronous because vanilla requires the result
before the corresponding content is drawn. In the lazy scan, vanilla also calls `LoadTiles` or
`LoadWall` and performs tree/palm style preparation before discovering that an entry has no paint.

## Alacrity method

The version-locked `alacrity-terraria-1.4.5.6-r13` patch adds one holder-private pending bit to each
`TilePaintSystemV2.ARenderTargetHolder` and changes the five shared request paths:

1. an unready, not-pending holder is queued once;
2. another request for the same holder while it is pending does not append another list entry;
3. the holder's injected clear helper clears the pending bit immediately before preparation;
4. `ARenderTargetHolder.Clear` calls that same helper, so reset/disposal cannot leave a holder stuck
   as pending.

This applies uniformly to tile, wall, tree-top, tree-branch, and cage-top holders. The patch also
lets `MakeExtraPreparations` return before its tree-only switch for ordinary tile types while the
policy is active. All tree-capable types retain the exact original method and foliage calculations.
For the six-tick lazy screen scan only, it now checks `Tile.color()` or `Tile.wallColor()` before
the associated asset request and tree/palm style work. Unpainted entries jump to the native next
stage; normal tile drawing and every non-lazy preparation path retain Terraria's original asset
loads.

## Behavior preserved

Kinesin does not change paint keys, paint colors, foliage selection, texture resolution, shader
parameters, render-target dimensions/contents, preparation order of distinct holders, tile/wall
asset semantics, RNG, multiplayer state, reset behavior, or graphics-device/content-loss behavior.
It does not prewarm ahead of the camera, move graphics work off-thread, split work across frames,
or allow a first-frame visual fallback.

## Expected improvement

For duplicate-heavy cold areas, the expensive pending preparation work changes from roughly the
number of painted instances to the number of unique unready holders. For example, 5,000 identical
painted stone tiles enqueue and prepare one holder rather than repeatedly preparing that holder.
This is expected to remove the dominant redundant CPU/asset/state-check portion of the observed
first-entry hitch in that case.

Areas with many genuinely new, distinct paint variants still pay vanilla's necessary synchronous
`RenderTarget2D` creation, binding, shader, and draw cost. The exact frame-time reduction therefore
depends on the ratio of duplicate instances to unique variants and must be measured in a live
painted world; no synthetic timing claim is presented as an in-game benchmark.

## Intentionally deferred

Incremental overlap scanning is not enabled because `PrepareForAreaDrawing` has no verified
tile-dirty generation at this hook. Reusing a previously scanned rectangle could skip a freshly
painted visible tile and violate vanilla first-frame correctness. Predictive prewarming, async
graphics work, budgeted preparation, paint-key scratch hash tables, and graphics-state batching
are likewise deferred until profiling proves they preserve the version-locked renderer contract.

## Follow-up investigation

The remaining candidate work was traced against Terraria 1.4.5.6 after the queue fix. None is
currently patched because its required behavior-preservation proof is absent or because it would
make the common path slower:

| Candidate | Verified result | Current decision |
| --- | --- | --- |
| Scan-local tile/wall field caching | The method reads `tile.type` more than once, but the extra reads are simple field loads. The dominant duplicate preparation path is already removed. | Do not add a fragile whole-method rewrite for a micro-optimization without a profile showing it matters. |
| Incremental overlap strips | The six-tick screen scan has no tile/paint dirty generation at this hook. A visible tile can change while remaining inside a previously scanned overlap. | Do not skip overlap; this would risk missing required synchronous preparation. |
| Repeated painted `LoadTiles` / `LoadWall` suppression | The color-zero lazy-scan case is now avoided. Suppressing later painted requests would need to preserve every asset-load failure and transition case. | Keep native calls for entries that may request a painted variation. |
| Per-scan paint-key cache | After holder-pending deduplication, repeated requests only perform a value-key dictionary lookup and a ready check. A reusable cache would add comparison/invalidation work to every painted tile. | Defer until a profile proves dictionary traffic remains material. |
| Color-zero foliage | Tree-top and tree-branch holders prepare a render target even at paint color zero; foliage rendering requests those variants through the same target path. | Preserve the requests; zero is a valid shader/foliage variant, not evidence of dead work. |
| `PrepareAllRequests` ready check | The request list retains capacity after `Clear`, and Kinesin now admits each unready holder once. Normal entries are therefore unready; another `IsReady` virtual call would be pure overhead. | Keep the direct ordered `Prepare()` loop. |

The next useful performance pass is live profiling of duplicate-heavy versus unique-heavy painted
areas. If unique holder generation dominates after this change, the remaining cost is the required
synchronous render-target/shader work rather than a safe CPU-side scan optimization.
