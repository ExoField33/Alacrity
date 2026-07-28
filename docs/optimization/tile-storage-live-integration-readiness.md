# Tile Storage: Live Integration Readiness

## Status

**Not ready for a live `Main.tile` replacement.** This is an intentional safe
failure, not a partial integration. The compact map foundation, behavior tests,
and version lock are ready to support a transform, but no transform has yet
accounted for the complete compiled `Tile[,]` and `Tile` reference surface.

The version-locked audit now classifies 11,781 of the 13,460 reads as direct
field/method operations, including argument-pushing setters and direct field
writes, plus 31 reads that are immediately discarded. The remaining 1,012
local-alias flows, 613 stack flows, and 50 tile-parameter calls now have an
executable-generated worklist. The control-flow stack pass completes without
state-limit failures and reduces them to explicit field reads/writes, tile
mutators, typed parameter escapes, argument/field/indirect escapes, and one
legacy-world-loader boxing boundary. They still require a complete
method-level transformation plan before any executable is written. This is a
hard entry gate, because treating either category as an ordinary value copy
would break mutations that currently retain a reference to the array element.

## Completed prerequisites

* The target is pinned to Terraria `1.4.5.6` with SHA-256
  `A89A24C6531D88A972662821044ACF1B3B5817621DD6C81D4BD7523BC4BBDDA9`.
* `TileData` is a compact, fourteen-byte value record with normal managed-array
  lifetime; it does not retain a pointer into movable managed memory.
* `AlacrityTileMap` uses one `TileData[]`, explicit materialization bits,
  snapshots, overlap-safe copies, region operations, and stale-world guards.
* Raw state, header semantics, copy/reset/clear behavior, paint/coating,
  selective clear masks, and compact snapshot fixtures are compared with the
  verified Terraria `Tile` implementation.
* The audit classifies every current `Tile[,]::Set` producer and 944 proven
  tile-null branches. It rejects changed hashes, array call shapes, field
  aliases, core tile method shapes, and serialization/network boundary shapes.
* Synthetic benchmarks are repeatable: warm-ups are discarded, medians are
  reported, and tile-access checksums must agree across samples.

## Blocking gates

| Gate | Current state | Required before live integration |
| --- | --- | --- |
| Compiled access migration | all 13,460 reads have an audited category; 1,675 identity/signature work items remain | semantic IL transformation plan for every access, typed local, field, parameter, return, and null branch |
| Reference identity | direct reference rotation and local/reference flows are audited only | explicit snapshot/materialization transformation and behavior tests for each classified case |
| World serialization | helpers are version-locked, not transformed | controlled game-hosted generated-world save/load semantic comparison |
| Tile network blocks | helpers are version-locked, not transformed | controlled game-hosted compression/decompression and tile-square parity fixtures |
| Rendering and simulation | direct call counts are inventoried | generated-world smoke tests for drawing, light scanning, liquid, wiring, framing, collision, map scans, and world generation |
| Patch safety | generic patch transactions exist | tile-specific transactional patch, verified output copy, rollback, and recovery test |
| User-data safety | no user data has been touched | test harness must use temporary generated data and a copied executable only |

## Fixture constraint

The standalone compatibility executable cannot safely call the vanilla world or
tile-block helpers: accessing `Terraria.Main` runs its 8,701-byte static
initializer. Save/load and packet fixtures must run inside a controlled,
game-hosted test process, never by opening a user world from a console test.

## Entry criteria for a live patch

A live transform may be considered only when all of the following are true:

1. The read-only audit passes for the exact target executable.
2. The transformer has one semantic rule for every audited operation and
   rejects any unrecognized instruction pattern.
3. A copy-only patch transaction validates the input hash, writes a separate
   output executable, and can restore its original bytes from a verified
   backup.
4. Controlled game-hosted world and packet fixtures prove semantic equality for
   all raw tile state, null/materialization behavior, and affected ordering.
5. Rendering/simulation smoke tests and measured benchmarks pass on the same
   transformed output.
6. A manual vanilla-server compatibility run and reconnect test pass using the
   transformed copy.

Until then, the production executable continues using vanilla `Tile[,]` and
there is no partial or fallback tile-storage mode to corrupt a world.

## Current residual worklist

The audit emits every residual flow with its type, method, and IL offset. The
local-alias subset currently has 796 direct field reads, 718 tile-method reads,
351 null checks, 145 typed-call escapes, 117 tile-method mutations, 106 direct
field writes, four local alias copies, and one return escape. Its former 84
control-flow uses are expanded by the stack pass into 150 field writes, 52
typed parameter escapes, 50 tile mutations, 31 field reads, 30 legacy boxing
uses, and a handful of named indirect/monitor/delegate boundaries. This
worklist is generated from the exact executable and is deliberately not
inferred from the decompiled source.

The caller audit also records 14,924 calls that cross a tile-typed signature,
including 194 calls to `Framing.GetTileSafely`. Each callee contract therefore
has an exact caller work item in the transformation ledger.

## Materialization constraint

The matched `Terraria.Tile` IL and readable source confirm that every bit in
the four existing header fields is used by vanilla state. A value-backed
replacement cannot hide a null/materialization marker in an existing header
bit. The live design must preserve materialization separately or add an
explicitly measured field, and each null branch must retain its original
polarity. In particular, `Framing.GetTileSafely` returns a fresh tile for
out-of-world coordinates but materializes an in-world null element; a
by-reference rewrite must preserve that distinction rather than sharing a
mutable fallback tile.

## Transformation artifacts

`tools/TileStorageTransform` emits a stable, version-locked operation ledger
(52,157 operations for the verified binary) and fails closed while any
operation lacks a verified lowering. The lowering preflight accepts every
currently observed 1.4.5.6 instruction shape; this confirms the audit is
complete, not that the lowerings have been implemented. Its copy-only
transaction accepts only a
separate output path, verifies the input hash before staging, will not
overwrite an existing output, and removes only an output whose generated hash
still matches its receipt. The command cannot currently create an executable:
the plan is intentionally not transform-ready and no lowerer has been
registered.

## Executable lowerer proof boundary

`tools/TileStorageTransform.Fixture` and the accompanying transform tests
exercise real Mono.Cecil rewrites on an isolated assembly. The direct Tile-field
instruction lowerer is executable and separately validated against a collectible
output assembly. The full fixture also attempts a rank-changing `Tile[,]` to
flat-value-array rewrite, but currently fails with a reproducible CLR signature
rejection once its compact copy path is included. It is retained as a narrow
failure fixture, not counted as evidence that a full storage replacement works.
The separate-output test discipline is proven; rank-changing storage migration
is not.

It is intentionally not registered for `Terraria.exe`. The real binary also
contains class-reference assignment, typed locals and fields, method signatures,
null/materialization branches, and object-runtime boundaries. Treating its
52,157 worklist entries as equivalent to the fixture would silently change tile
identity or mutation behavior. The production transformer remains blocked until
those operations have exact semantic lowerings and controlled game-hosted parity
tests.
