# Tile Storage: Compatibility Contract

## Non-negotiable external behavior

The optimized representation is internal only. It must not change:

* vanilla `.wld` file format or tile serialization ordering;
* vanilla server packet formats, section synchronization, or tile-square
  messages;
* player, world, achievement, or plugin data formats;
* the public PluginSdk surface;
* tile, liquid, wiring, actuator, slope, coating, paint, framing, lighting,
  collision, map, or rendering behavior.

No custom content registry, mod-defined tile state, item storage, projectile
storage, wall remapping, or tModLoader-style extensibility will be introduced.

## Verified 1.4.5.6 representation dependencies

| Dependency | Required preservation |
| --- | --- |
| `Main.tile : Tile[,]` | All transformed accesses must retain equivalent bounds and data behavior. |
| `WallDrawing._tileArray : Tile[,]` | Its aliasing/assignment behavior must be mapped to the active store. |
| `Tile` instance fields | Each field and bit in the four headers must round-trip exactly. |
| `Tile` methods | Clear/copy/reset/header helpers must retain their visible effects. |
| Null slots | Lazy construction and read-only absence checks must remain distinct where observable. |
| `new Tile(source)` / `CopyFrom` | Snapshot copy and in-place content copy must not be conflated. |
| world reset/reconnect | No handle from an old map can write into a new map. |

## Serialization and networking plan

World loading and saving must continue to use Terraria's existing byte-level
serialization logic after it has been adapted to read/write `TileData`. The
implementation must compare decoded tile state, not merely file hashes, because
world metadata can legitimately vary between saves.

Likewise, packet writers/readers must continue emitting and consuming the
current Terraria formats. The backing store may change, but the field order,
flag interpretation, section boundaries, and packet semantics may not.

`tools/TileStorageCompatibilityTests` directly references the verified
Terraria 1.4.5.6 assembly and round-trips every raw `Tile` field through
`TileData`. It also compares `CopyFrom`, `ClearEverything`, and `ClearTile`
against real tile instances, including the latter's intentionally partial
header-bit reset. It is intentionally a field-parity test, not a world-file or
multiplayer test; those remain gated on the live transform.
The test targets x86 because the supported Terraria/XNA runtime is loaded in
that process architecture.

### Version-locked serialization and packet boundaries

The audit additionally requires these exact helpers in the supported executable
before any future transformer may touch live storage. Their signatures and IL
sizes are inventory identifiers, not replacement targets in this phase.

| Boundary | Metadata token | IL bytes | Responsibility |
| --- | ---: | ---: | --- |
| `WorldFile.SaveWorldTiles(BinaryWriter)` | `0x1535` | 1,067 | vanilla world tile writer |
| `WorldFile.LoadWorldTiles(BinaryReader, bool[])` | `0x153F` | 1,012 | current world tile reader |
| `WorldFile.LoadWorld_Version1_Old_BeforeRelease88(BinaryReader)` | `0x1556` | 3,652 | legacy world reader |
| `NetMessage.CompressTileBlock_Inner(BinaryWriter, int, int, int, int)` | `0x02B0` | 2,190 | section/tile-block writer |
| `NetMessage.DecompressTileBlock_Inner(BinaryReader, int, int, int, int)` | `0x02B2` | 1,119 | section/tile-block reader |
| `NetMessage.SendTileSquare(int, int, int, int, int, TileChangeType)` | `0x02BC` | 23 | tile-square dispatch |

These methods remain unmodified. A future migration must preserve their byte
formats exactly and compare decoded tile state rather than inventing an
Alacrity-specific world or packet format.

### Isolated-fixture gate

The standalone compatibility harness intentionally does not invoke these
methods. Accessing `Terraria.Main` executes the verified 8,701-byte static
initializer, which owns broader game/runtime initialization and cannot be
treated as an isolated tile fixture in a console test process. The required
save/load and packet parity fixture must therefore run in a controlled,
game-hosted process against generated in-memory or temporary test data only.
It must never open a user world or write to the installed executable.

Required semantic cases include:

```text
active/inactive tiles, types, walls, frames,
all four wires, actuators, slopes, half blocks,
liquid amount/type, paints, invisible/fullbright coatings,
tile-square updates, section updates, reconnect, and world reset.
```

## Value-type migration blockers

A direct class-to-struct conversion cannot be treated as a metadata-only
change. The verified executable exposes five `Tile`-returning methods:

* `Framing.GetTileSafely` for `Point`, `Vector2`, `Point16`, and `(int,int)`;
* `Player.GetFloorTile(int,int)`.

It also contains `Tile&` parameters in physics and player-sitting paths, plus
166 total method signatures and four fields that contain a `Tile`. A tile
returned from `GetTileSafely` is currently an alias to the corresponding array
object when it is in-bounds; a value copy would silently discard later writes.
The transformed representation must therefore either preserve that alias with a
managed interior reference or rewrite each return/call chain to perform an
explicit write-back. The latter is not eligible for a mechanical bulk rewrite
until method-level dataflow validates every mutation path.

The target also relies on `null` to represent an unmaterialized coordinate.
`new Tile()` and `new Tile(null)` create a non-null, zero-valued tile, while a
new `Tile[,]` contains null references. A compact value representation needs a
separate materialization bit or equivalent state; raw zero fields alone cannot
preserve both meanings.

## Compatibility modes

During development only, a version-gated launch switch may select original or
optimized storage on generated test data. It must never silently fall back from
an enabled optimized build to a partially transformed executable. A missing or
ambiguous IL match is a hard failure before launch.

No permanent dual production implementation is planned unless behavioral
comparison proves it is necessary.

## Test boundaries

All tests use generated worlds and temporary directories. No test may open,
write, migrate, or delete a real user world. Multiplayer checks must use a
controlled vanilla-compatible test server or packet fixtures; they must never
alter network packets.

## World-lifetime protection

The internal map foundation has a host-owned generation counter. Transient tile
handles carry only coordinates and the active generation; a world replacement
or reset invalidates prior handles before they can access a new map. This is an
internal debug/safety mechanism, not a PluginSdk API and not a persistent
pointer into tile storage.

## Compatibility risks

1. **Reference identity:** vanilla code can retain a `Tile` reference. A value
   conversion must classify and transform every such flow.
2. **Null meaning:** default data is not always synonymous with a formerly null
   slot.
3. **Multidimensional array calls:** `Get`/`Set` are compiled method calls, so
   replacing only the field type would create invalid IL.
4. **Private aliases:** `WallDrawing._tileArray` shows that `Main.tile` is not
   the only typed storage reference.
5. **Version drift:** the executable hash is a strict patch precondition; a
   Terraria update requires a new inventory and transform validation.
6. **Reference rotation:** `WorldUtils.DebugRotate` rotates `Tile` references,
   while `WorldGen.clearWorld` assigns `null`. These must become deliberately
   specified value-copy and unmaterialization operations, not generic `Set`
   replacements.

These risks are why no live replacement is made during the inventory phase.

## Verified exceptional stores

The IL audit reduces the non-default `Tile[,]::Set` cases to explicit future
transform rules rather than a generic assignment rewrite:

| Verified location | Vanilla behavior | Required flat-map operation |
| --- | --- | --- |
| `WorldGen.clearWorld` | stores `null` | clear data and clear the materialization bit |
| `NetMessage.DecompressTileBlock_Inner` | `new Tile(existing)` | snapshot-copy the existing coordinate into a materialized destination |
| `WorldBuilding.WorldUtils.DebugRotate` | moves three tile references | take snapshots before writes, then perform the documented rotation |
| `WorldGen.CountTiles` | duplicates a default-tile construction/store | preserve the original allocation/order behavior with explicit materialization |

The other 336 constructor stores and 37 local stores remain source-level
migration work. A future transformer must preserve their control-flow order;
it may not collapse them into a blanket eager materialization pass.
