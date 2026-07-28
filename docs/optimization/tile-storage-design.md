# Tile Storage: Design and Decision Gates

## Goal

Replace the vanilla object-per-tile representation with one host-owned,
contiguous value array while preserving vanilla serialization, packet formats,
and observable gameplay behavior. This is a foundational Terraria integration
change; it is not a PluginSdk service and must not be exposed as mutable plugin
state.

## Intended backing store

The initial representation is array-of-structs, not a speculative
structure-of-arrays rewrite:

```csharp
internal sealed class AlacrityTileMap
{
    private readonly TileData[] _data;
    private readonly int _width;
    private readonly int _height;

    internal ref TileData GetDataUnchecked(int x, int y)
    {
        return ref _data[x + y * _width];
    }
}
```

`TileData` will contain exactly the verified vanilla instance fields from
`Terraria.Tile`, using the same signedness. Its final managed size and field
layout must be measured by tests before it is used by a patched executable.
No explicit packing, persistent pinning, raw pointer retention, or per-tile
map reference is planned.

## Chosen compatibility direction

The actual executable uses `Tile[,]::Get` and `Tile[,]::Set` directly. A
managed facade, indexer, or a separate sidecar map cannot satisfy that compiled
field contract. Retaining `Tile[,]` plus per-tile wrappers would keep the large
reference array and object allocations, missing the objective.

Therefore, the only design that can meet the requested memory goal is a
version-gated, semantic IL transformation performed on a copy of the verified
Terraria executable. It must transform, as one transaction:

1. `Main.tile` and every other `Tile[,]` signature owned by Terraria.
2. All multidimensional `Get`/`Set` call sites.
3. Tile construction, null comparison, reference assignment, and local-array
   patterns whose behavior changes with value storage.
4. Tile field and method accesses so they operate on the backing data rather
   than detached objects.

This is intentionally **not** an instruction-index patch. It requires semantic
matching against the exact 1.4.5.6 executable hash and must fail closed when an
expected pattern is missing or ambiguous.

### Handle compatibility model

The live transform will not convert `Tile` into a raw `TileData` value. It will
use a small `Tile` handle value that represents either a map/index pair or a
separately allocated standalone tile. The flat map stores only `TileData[]`;
it never stores a handle, map reference, or object per coordinate. Copying a
map-backed handle keeps the same map/index identity, so field writes through a
local or parameter continue to update the backing world tile. Copying a
standalone handle keeps the same small holder object, preserving the identity
of `new Tile()` outside the map. This makes it possible to retain value-style
method signatures while preventing detached copies and avoiding persistent
pointers into the managed data array.

## Null model

A value store has no native `null`. Default `TileData` is compatible with the
field values of `new Tile()`, but it is not automatically compatible with
vanilla's distinction between an unmaterialized array slot and a materialized
default tile.

The implementation must first classify each null use as either:

* a lazy-materialization guard;
* an absence/read guard; or
* an identity/reference-flow operation.

If the distinction remains observable after transformation, an internal,
compact materialization bitmap is required. It must be host-owned and excluded
from save/network formats. It must never be exposed through PluginSdk.

## Copy model

The internal map will expose only explicit host operations:

```text
GetTileSnapshot
SetTileData
CopyTileData
ClearTile
ClearRegion
CopyRegion
ClearAll
```

Overlapping region copies must define their direction or use an intermediate
buffer. Ordinary wrapper assignment must not silently change from reference
copy semantics to content copy semantics.

## Lifecycle

`AlacrityTileMap` will be created during validated world initialization,
replaced at reset/reconnect, and released during unload. A generation token is
recommended for debug builds so stale access handles from an old world fail
diagnostically rather than addressing a new world.

## Safety and performance rules

* `TileData[]` is a normal managed array; no persistent pointers into it.
* Hot access must have no allocations, boxing, reflection, locks, LINQ, or
  virtual dispatch.
* Bounds checks may be centralized for public/debug paths, but validated loops
  must not add redundant work in release hot paths. `GetDataUnchecked` returns
  a compact value for internal host-owned, already-bounded read loops; writes
  must use explicit materialization or map operations.
* Bulk work should use `Array.Clear`, spans, or bounded loops only after
  overlapping-copy behavior is tested.
* No plugin can replace or mutate the active storage directly.

## Reference implementation boundary

The local TerraAngel source confirms the useful part of its design: vanilla
tile state can be represented as a compact value record and accessed through a
map abstraction. It is not copied directly. Its older implementation uses a
two-dimensional `TileData[,]` and an unsafe `Tile` wrapper that retains a
`TileData*` created from a managed-array reference via `Unsafe.AsRef`.

Alacrity deliberately rejects that wrapper approach. A pointer into an ordinary
movable managed array is not an acceptable long-lived ownership boundary here.
The Alacrity design instead uses a one-dimensional managed `TileData[]`, map
indices, short-lived `ref` access only within a call, and explicit snapshot
operations. The data-oriented principle is retained without importing
TerraAngel's pointer lifetime risk or its older-version source patch set.

## Decision gates before a live replacement

No executable transformation may be installed until all gates pass:

1. A generated IL coverage report classifies every `Tile[,]` `Get`/`Set`,
   constructor, null comparison, and typed local/field signature.
2. A patch transaction can prove the exact input hash and produce a rollback
   journal before writing an output copy.
3. A generated-world test harness validates field, copy, default/null,
   save/load, and packet semantic parity without touching user data.
4. Rendering, lighting, liquid, wiring, framing, collision, and world-gen
   smoke tests pass on the patched output.
5. Benchmarks show a measured benefit against the original executable path.

Until these gates pass, the storage design remains a documented integration
plan rather than a safe production patch.

## Audit tool

`tools/TileStorageAudit` is the first gate implementation. It is read-only and
requires the verified executable hash. It writes a JSON report containing each
method's `Tile[,]` call shapes, direct `Main.tile` references, tile constructors,
candidate null branches, local array declarations, typed fields, and unsupported
array method calls. A non-zero unsupported-call result blocks future transform
work rather than allowing a partial rewrite.

The initial verified run against the supported executable found `Get`, `Set`,
and one array constructor only; it found no `Address` or unknown array call
shape. It also produced 4,377 candidate null branches. Those branches are not
automatically safe to rewrite: the next phase must distinguish lazy allocation,
absence tests, and ordinary control flow using local dataflow rather than a
proximity heuristic.

The audit also classifies each `Set` tile producer with exact local IL context:
default constructor, copy constructor, null, direct `Get`, local, parameter, or
field. An unclassified producer is an audit failure, not a transformation
fallback. This is the first explicit guard against silently changing reference
assignment into a detached value copy.

Its next gate records proven null checks rather than proximity candidates. On
the supported executable that gate found 944 branch sites, split between direct
and local tile values. The eventual transform must replace each verified branch
with a materialization-bit test that preserves its true/false polarity. It may
not rewrite every `Get` as though it were a null check.

The same audit now emits the full `Tile` reference surface: typed fields and
method signatures, typed local count, and direct field/method call frequencies.
The future transformer will consume this report as an allow-list. Any new
reference surface in a different Terraria build is a patch precondition failure.
The audit additionally validates the known 1.4.5.6 baseline counts and aliases
before it writes a report. The executable hash is the primary lock; these
semantic counts are a second guard against an accidental tool or input change.

The compact foundation also mirrors the verified 1.4.5.6 behavior of
`Tile.CopyFrom`, `Tile.ClearEverything`, `Tile.ClearTile`, and `Tile.ClearSlope`.
`ClearTile` is intentionally not a full reset: it clears only slope,
half-brick, active, and inactive bits while preserving the other raw state.
The audit validates the executable method call/field-write shapes and the
compatibility tool compares each operation with an actual `Terraria.Tile`.

The internal `TileData` foundation now also implements the verified compact
semantics for primary headers (activity, wires, slopes, actuator, paint, and
fullbright wall), liquid/wire headers, and frame/coating headers. The direct
compatibility harness exhausts every raw header value for the relevant
getter/setter groups. These are preparation-only helpers: no live Terraria
call site uses them until the version-locked transform can cover the complete
`Tile[,]` and `Tile` reference surface.

Selective tile clearing is represented internally by `TileDataMask`, whose
bit values are locked to the 1.4.5.6 `TileDataType` values through the direct
compatibility tests. This avoids coupling the storage core to a Terraria type
while preserving the exact behavior required by future transformed call sites.
