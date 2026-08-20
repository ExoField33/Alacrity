# Lighting Parallelism

Kinesin 0.9.0 adds **Optimize Lighting Parallelism**, enabled by default. It requests the generic
`context.Terraria.RenderingOptimizations` `LightingParallelism` policy; Kinesin has no Terraria or
XNA reference and receives no lighting state.

## Vanilla work

Terraria 1.4.5.6 uses `FastParallel.For` for two directional `LightMap.BlurPass` ranges and for
`TileLightScanner.ExportTo`'s independent tile columns. These callbacks write disjoint lines or
columns, but the vanilla scheduler can leave the calling thread waiting while queued work runs.

## Alacrity method

The version-locked patch replaces only those three scheduling calls. The native blur and export
callbacks, both blur passes, masks, decay, tile order inside each column, random modifiers, and
lighting cadence remain unchanged. The integration executor partitions each half-open range into
the same balanced contiguous ranges as vanilla, queues its non-caller ranges in the same order,
and runs vanilla's first range on the calling thread. Its worker groups and typed callback invokers
are retained after first use, avoiding per-lighting-pass task or barrier allocation.

The executor uses the same `max(1, Environment.ProcessorCount - 1)` degree as Terraria's
`FastParallel`, so it does not intentionally oversubscribe the CPU. A disabled policy,
unavailable bridge, or stale bridge call falls directly back to Terraria's original
`FastParallel.For` at the generated wrapper.

## Expected result

This reduces scheduling and idle-wait overhead, especially on multi-core systems where lighting
areas are large enough to justify parallel work. It does not reduce the amount of light scanning or
blur math, so the exact gain depends on CPU topology, current lighting area, and lighting mode.
Manual comparison should show identical light propagation, liquid light flicker, and tile lighting.
