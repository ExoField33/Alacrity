# Waterfall Presentation

Kinesin 0.4.0 adds a conservative, default-enabled optimization for Terraria 1.4.5.6's
`WaterfallManager` renderer. It is exposed as **Optimize Waterfall Rendering** in Kinesin's
settings and is requested through the generic `IPluginRenderingOptimizationService`; Kinesin
never receives Terraria or XNA renderer objects.

## Vanilla work

For every waterfall segment, vanilla repeatedly calls `TileBatch.SetLayer` with the same layer
and stack values. It also calls `WorldGen.SolidTile(Tile)` several times while resolving each
live waterfall route. That helper intentionally contains a broad exception guard, which is a
reasonable public-world helper but unnecessary for WaterfallManager's already initialized,
in-world tile references. When discovery finds no waterfalls, `Draw()` can still enter each
active liquid-style pass and execute `DrawWaterfall` setup despite having no segments.

## Alacrity path

The version-locked patch captures the tile array and camera values once per `DrawWaterfall` call,
then uses those frame-local values through the unchanged native route code. It also caches the
currently selected waterfall `TileBatch` layer only within that call. The first selection and each
layer transition still call vanilla `SetLayer`; repeated identical selections are skipped. Finally,
the patch uses an equivalent guarded solidity test for verified non-null waterfall tiles while the
policy is enabled. Out-of-range or uninitialized state returns the same `false` result as vanilla's
guarded helper.

When `currentMax` is zero, the patch preserves Terraria's ambient-waterfall, ambient-lavafall,
lava flag, and temporary tile-solid reset state before returning ahead of the empty route-loop
setup. This targets scenes with no discovered waterfalls without changing liquid-style or ambient
state behavior.

Vanilla also rescans the expanded waterfall source area every thirty updates while idle. Kinesin
remembers the most recent native discovery result and reuses it only when the tile array, camera
position, resolution, graphics quality, and waterfall limit are unchanged, and both liquid work
queues are idle. Local placement, removal, replacement, slopes, half-brick changes, actuation,
and received multiplayer tile changes mark the cached result dirty. The two native liquid-work
admission paths (`Liquid.AddWater` and `LiquidBuffer.AddBuffer`) also invalidate the cache before
simulation begins, so a liquid change that settles before the next scan cannot be mistaken for an
unchanged scene. A forced lookup or any active liquid simulation always executes Terraria's native scan. The cache stores the original native
waterfall array rather than new geometry, so route calculation, ordering, lighting, biome passes,
animation, sound inputs, sparkle decisions, and `Main.rand` call order remain vanilla-owned.

## Disabled Policy

Kinesin is installed as a version-locked presentation optimization. Disabling this policy means
vanilla-observable behavior: native waterfall routing, ordering, lighting, effects, and random
number ordering remain authoritative. The generated executable may still contain private helpers
and invalidation bookkeeping, but policy-off execution takes Terraria's native path.

## Expected impact

The common benefit now also covers idle scenes: after the first native discovery, an unchanged
screen with settled liquid avoids the full expanded tile scan entirely. The steady-state path is
allocation-free. Actual frame-time improvement depends on the scene; the remaining unavoidable
cost is Terraria's real segment drawing, lighting, and sparkle work.
