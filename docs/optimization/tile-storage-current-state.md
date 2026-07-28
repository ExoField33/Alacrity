# Tile Storage: Current State

## Authority and scope

This inventory was taken from the local Terraria 1.4.5.6 executable, not from
the decompiler alone.

| Item | Value |
| --- | --- |
| Executable | `Alacrity/Terraria.exe` |
| Assembly version | `1.4.5.6` |
| SHA-256 | `A89A24C6531D88A972662821044ACF1B3B5817621DD6C81D4BD7523BC4BBDDA9` |
| Readable reference | `Alacrity/Decompiled Terraria 1.4.5.6` |

The decompiled source and executable agree on the important storage surface:
`Terraria.Main.tile` is a public static `Terraria.Tile[,]` field.

## Vanilla Tile shape

`Terraria.Tile` is a mutable class. Its instance state is:

```text
ushort type
ushort wall
byte liquid
ushort sTileHeader
byte bTileHeader
byte bTileHeader2
byte bTileHeader3
short frameX
short frameY
```

Those fourteen logical bytes encode tile/wall IDs, frame coordinates, liquid,
paint, coatings, wires, actuator state, slope/half-block state, and related
framing flags. The class also contains behavior such as `CopyFrom`, `Clone`,
`ClearEverything`, `ClearTile`, `Clear`, `ResetToType`, and header bit helpers.
The logical field total is not an object-size measurement: the current design
also pays for object headers, alignment, a two-dimensional reference array,
and one object allocation for each materialized tile.

## Executable access inventory

Mono.Cecil inspection of the verified executable found:

| Pattern | Count |
| --- | ---: |
| Methods referencing `Main.tile` | 1,221 |
| `Tile[,]::Get(int,int)` calls | 13,460 |
| `Tile[,]::Set(int,int,Tile)` calls | 379 |
| `Tile[,]::Address(int,int)` calls | 0 |
| `Tile[,]` allocations | 1 |
| Methods constructing `Terraria.Tile` | 135 |
| Fields typed `Tile[,]` | `Main.tile`, `WallDrawing._tileArray` |

The generated audit reports 1,278 methods with a tile-array, direct tile-field,
or tile-constructor dependency. It found 4,377 candidate null-branch patterns
that require semantic classification before transformation. It found no
unsupported tile-array method call shapes. The dominant owners of `Main.tile` references are `WorldGen` (443 methods),
`Player` (105), `NPC` (42), `Collision` (40), `Projectile` (40),
`SmartCursorHelper` (36), `TileDrawing` (34), `Wiring` (24), `Liquid` (14),
`WorldFile` (9), and `TileLightScanner` (6). This is an assembly-wide runtime
representation, not a local initialization detail.

## Behavioral categories

The following systems directly depend on the present array/reference behavior:

| Area | Representative code | Compatibility concern |
| --- | --- | --- |
| Initialization and resize | `Main`, `WorldFile` | Creates `new Tile[maxTilesX,maxTilesY]`; world dimensions determine bounds. |
| Lazy initialization | `Framing.GetTileSafely`, `Collision`, `Gore`, `Minecart`, `WorldGen` | `null` means not materialized; callers may allocate and store a new tile. |
| Save/load | `Terraria.IO.WorldFile` | Reads and writes tile state in vanilla ordering. |
| Network sections | `MessageBuffer`, `NetMessage` | Section and tile-square packets must retain their existing byte format. |
| Rendering and lighting | `TileDrawing`, `WallDrawing`, `TileLightScanner` | Reads frame, paint, coating, wall, liquid and header flags in hot loops. |
| Simulation | `Liquid`, `Wiring`, `Collision`, `Framing`, `WorldGen` | Mutates tile data and assumes reference/null behavior. |
| Map and tile entities | map generation and `GameContent.Tile_Entities` | Reads the same state during scans and synchronization. |

Examples of distinct null semantics already present in vanilla:

* `Framing.GetTileSafely` materializes and stores a tile when the slot is null.
* Collision and gore paths materialize neighboring tiles before mutation.
* Read-only paths use a null tile to mean absent/uninitialized and return early.
* Some rendering/simulation code creates a local default `Tile` without storing
  it in `Main.tile`.

These cases cannot be replaced by simply deleting null checks.

## Integration readiness matrix

The current verified audit records these direct `Tile[,]` operations for the
subsystems that must be migrated together with any live replacement. The counts
are an implementation inventory, not permission to transform one subsystem in
isolation.

| Subsystem | Methods | Gets | Sets | Required future proof |
| --- | ---: | ---: | ---: | --- |
| `WorldGen` | 555 | 9,151 | 232 | generated-world parity and initialization/order checks |
| Rendering (`GameContent.Drawing`) | 36 | 112 | 6 | visual/frame/paint/coating parity at all zoom levels |
| `Liquid` | 15 | 134 | 0 | liquid update and settle behavior |
| `Wiring` | 24 | 129 | 0 | wire, actuator, and mechanism behavior |
| `Collision` | 42 | 296 | 12 | movement and ray/collision parity |
| `Framing` | 2 | 9 | 1 | lazy materialization and framing parity |
| Mapping (`MapHelper`) | 1 | 1 | 0 | map-tile state parity |
| Lighting (`Graphics.Light.TileLightScanner`) | 6 | direct field access | 0 | tile-light scan parity |

The rendering hot spots begin with `TileDrawing.DrawBasicTile` (15 array reads),
`WallDrawing.DrawWalls` (9 reads/1 write), and
`TileDrawing.DrawTile_LiquidBehindTile` (4 reads/4 writes). Simulation hot
spots include `Liquid.Update` (65 reads), `Wiring.HitWireSingle` (35),
`Wiring.HitSwitch` (34), and `Collision.CanHit`/`CanHitWithCheck` (33 each).
`WorldGen` is the dominant migration surface and cannot be deferred as a
post-integration cleanup.

## Reference-flow inventory

The verified IL additionally contains 1,088 locals typed `Terraria.Tile`, 164
tile parameters, five tile return values, and four fields typed `Terraria.Tile`.
Hot direct field accesses include `type` (6,938), `wall` (1,996), `frameX`
(1,902), `frameY` (1,548), and `liquid` (1,019). Common tile helper calls are
`active` (3,438 calls across getter/setter overloads), `nactive` (355), `slope`
(466), `halfBrick` (431), and the tile header/wire/coating helpers.

Of the 379 `Tile[,]::Set` calls, 372 are backed by a `new Tile(...)` in their
local IL context. The remaining cases include chest-local tile placement,
`WorldGen.clearWorld` assigning `null`, and `WorldUtils.DebugRotate` rotating
tile references. A transformation must preserve the latter as explicit snapshot
or unmaterialization operations; it must not assume every `Set` means an
allocation. `WorldGen.CountTiles` also has a deliberate duplicate/new/store
sequence: one newly allocated tile is both saved to `Main.tile` and retained in
a local. The migration plan treats this as a separate materialize-and-snapshot
pattern.

The strict audit classification has no unresolved store producer. Its immediate
producer categories are 336 default constructors, one copy constructor, one
duplicated default constructor, one null clear, three direct source-tile reads,
and 37 typed locals. The output uses string enum values so a future transform
transaction can require an approved category rather than relying on a numeric
code.

The earlier broad proximity scan found 4,377 possible null-adjacent paths. The
conservative dataflow pass reduces that to 944 verified tile-null branches:
437 direct true branches, 208 direct false branches, 113 local true branches,
and 186 local false branches. The compiler emits these as direct branch forms
for this executable rather than explicit `ldnull`/`ceq` sequences. Any future
transformer must use this exact audited set and leave unproven control flow
untouched.

## Copying semantics

The codebase uses several materially different operations:

* `new Tile(source)` and `Clone()` copy tile state into a new object.
* `CopyFrom` mutates the existing target object.
* `Main.tile[x, y] = tile` changes an array reference, not merely field values.
* Clear methods reset subsets or all of the represented state.

An indexed value store must make each of these operations explicit. A copied
handle must never silently become a detached copy of mutable tile state.

## Existing Alacrity impact

The current Alacrity source and tooling have no production tile-storage patch
or `Main.tile` mutation path. The existing executable patch tooling is focused
on unrelated version-gated patches. This migration therefore belongs in the
Terraria integration/patching boundary, not PluginSdk or a feature plugin.

## Conclusion

The primary memory opportunity is real, but `Tile[,]` compatibility is the
central engineering problem. A facade cannot be assigned to a field whose
compiled type is `Tile[,]`; every access signature and reference/null operation
that depends on that field must be accounted for before a live replacement is
attempted.
