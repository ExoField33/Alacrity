# Tile Storage: Benchmark Plan

## Rule for reporting

This document intentionally contains no improvement claim. The optimized path
does not exist yet, so all result cells remain pending. Measurements will be
recorded only after the original and transformed paths have passed the same
semantic test suite.

## Environments

Record for every run:

```text
Terraria executable SHA-256
Alacrity patcher version
runtime and architecture
CPU, RAM, operating system build
world dimensions and seed
graphics settings
run count, warm-up count, and median
```

Use generated small, medium, and large worlds. Never benchmark against a user
world.

## Required measurements

| Scenario | Original | Optimized | Absolute difference | Percentage |
| --- | ---: | ---: | ---: | ---: |
| Process memory after startup | pending | pending | pending | pending |
| Memory after small-world load | pending | pending | pending | pending |
| Memory after medium-world load | pending | pending | pending | pending |
| Memory after large-world load | pending | pending | pending | pending |
| World-load time | pending | pending | pending | pending |
| World-generation time | pending | pending | pending | pending |
| Full-map clear | pending | pending | pending | pending |
| Region copy | pending | pending | pending | pending |
| Random tile access | pending | pending | pending | pending |
| Sequential iteration | pending | pending | pending | pending |
| Rendering-adjacent iteration | pending | pending | pending | pending |
| GC allocations during load | pending | pending | pending | pending |
| GC collections during generation | pending | pending | pending | pending |

## Collection method

* Process memory: record private bytes and managed heap separately.
* Managed allocations: use controlled `GC.GetAllocatedBytesForCurrentThread`
  fixtures where supported and retain GC collection counts.
* Timings: use a monotonic stopwatch, discard warm-up runs, record medians and
  variance.
* Tile map microbenchmarks: validate all indices before the timed loop so the
  benchmark measures storage access rather than test scaffolding.
* Integration benchmarks: run only after save/load and packet fixtures prove
  semantic equality.

`tools/TileStorageBenchmarks` supplies an early synthetic allocation/access
model. It intentionally compares a generated legacy object grid against the
internal flat map only; its output is not a Terraria memory or gameplay claim.
It exists to protect flat-map allocation and indexing regressions while the live
integration benchmark remains unavailable.

## Synthetic regression snapshot

The following warm-process median was captured with
`TileStorageBenchmarks 640 360 250000 2 7`: two discarded warm-ups and seven
measured samples. It is retained solely to detect regressions in the isolated
map; it must not be presented as a live Terraria result.

| Model | Thread allocations | Heap delta | Sequential checksum |
| --- | ---: | ---: | ---: |
| Generated object grid | 9,216,144 bytes | 9,244,808 bytes | 399,704,904 |
| `AlacrityTileMap` | 3,254,600 bytes | 3,262,696 bytes | 399,704,904 |

Timing values remain environment-sensitive and are intentionally not presented
as an optimization claim. The runner reports warm-up-discarded medians in its
JSON output for regression investigation.

## Acceptance interpretation

The primary success condition is removal of the reference grid and per-tile
managed allocations while retaining behavior. A memory reduction must be
measured, not inferred from the logical `TileData` size. If a transformed path
regresses world load, rendering-adjacent access, or generated-world behavior,
it is not ready for production regardless of theoretical savings.
