# Clothing Entity Presentation

## Scope

Kinesin's **Optimize Clothing Entity Rendering** setting is enabled by default. It requests the
generic `context.Terraria.RenderingOptimizations` `ClothingEntityPresentation` policy. The
version-locked host patch only changes the local draw preparation for hat racks and display dolls;
plugins never receive Terraria tile entities, equipment data, shaders, or XNA rendering objects.

## Vanilla behavior

During a solid-tile draw, `TileDrawing` discovers visible display dolls and hat racks and records
their top-left tile position in `_displayDollTileEntityPositions` and
`_hatRackTileEntityPositions`. The dictionary value is the matching `TileEntity.ID`, or `-1` when
no matching entity currently exists.

Both dictionaries begin with Terraria's default zero capacity. The first dense room therefore
causes several allocation-and-rehash growth steps while every visible clothing object is entered.
Vanilla also invokes `TEHatRack.Draw` for racks whose two item slots and dye slots are empty. That
method performs two full player-renderer setup/draw passes even though `isHatRackDoll` makes every
corresponding draw color transparent.

At `PostDrawTiles`, vanilla draws each dictionary in a separate `SpriteBatch.Begin`/`End` pair.
For every entry it discards `-1` values, hashes the position again through
`TileEntity.TryGetAt<T>(x, y, out entity)`, checks the resulting type, and then calls the entity's
existing `Draw` method.

The two dictionaries are explicitly cleared at the start of every solid-tile draw and rebuilt
while tiles are drawn. That makes a persistent reference cache unsafe: it could retain a removed,
replaced, multiplayer-updated, or no-longer-visible entity.

## Alacrity method

When the policy is active, the patched `PostDrawTiles` path uses optimized hat-rack and
display-doll draw paths. Each returns before opening its `SpriteBatch` pass when its current
visible dictionary is empty, and otherwise opens the exact vanilla `SpriteSortMode.Immediate`
pass with the same blend, sampler, depth, rasterizer, effect, and transform values.

The two visible dictionaries reserve space for 2,048 entries at `TileDrawing` construction. They
are still cleared and repopulated by Terraria every solid draw; the reservation only prevents the
first dense viewport from paying repeated dictionary growth/rehash allocations. In the optimized
hat-rack loop, `TEHatRack.ContainsItems()` suppresses the two player-renderer calls only when both
item and dye slots are empty. Terraria's own hat-rack draw state makes that exact case fully
transparent, so the tile appearance is unchanged.

The generated loops use each dictionary's already-captured `TileEntity.ID` and call
`TileEntity.TryGet<T>(id, out entity)`. This removes the second position-key construction and
`ByPosition` hash lookup while still validating the type against Terraria's live `ByID` table on
the current frame. Entries whose ID is `-1`, removed entries, and replaced entities are skipped
safely. Hat racks remain before display dolls, matching vanilla ordering.

Dense rooms can expose a cold `LegacyPlayerRenderer`/content path: display dolls and occupied
hat-rack slots invoke Terraria's full fake-player preparation and draw sequence, including the
same asset and dye/shader access that real player rendering uses. That sequence is not safe to run
on a worker: it mutates the tile entity's fake `Player` and touches Terraria content and XNA state.
Alacrity therefore preserves every native clothing draw. It does not defer, cache, or suppress a
visible mannequin or hat rack while attempting to smooth this cold path.

During `CacheSpecialDraws_Part1`, each visible segment of a display doll or hat rack resolves to
the same top-left entity point. Vanilla still hashes that point through `ContainsKey` for every
segment. The optimized path captures the policy once for the solid draw and remembers only the
immediately preceding display-doll and hat-rack point. Consecutive segments with the same point
skip that redundant dictionary lookup; the first segment continues through the exact native
discovery, ID lookup, and legacy-special registration path. This is frame-local state, reset with
the native dictionaries, so it cannot retain stale entities.

The two vanilla batch boundaries are deliberately retained. Although their outer `Begin` arguments
match, their nested player/equipment renderer can affect shader state and has not proved a merged
boundary equivalent. `SpriteSortMode.Immediate` is preserved because equipment dyes and shaders
can require per-draw effect state. `TEHatRack.Draw` and `TEDisplayDoll.Draw` are left unmodified: their
lighting, vanity equipment, dyes, animation, pose, content lookups, and multiplayer state remain
Terraria-owned.

## Expected improvement

For every visible clothing entity, the optimized path removes the repeated position-based tile
entity lookup. Each empty current collection avoids its own `SpriteBatch.Begin` / `End` pair.
The first dense viewport avoids the normal dictionary growth/rehash chain, and empty hat racks
avoid two otherwise transparent player-renderer passes. The exact improvement depends on the mix
of empty/occupied racks and display dolls; the unavoidable display-doll and occupied-rack draw
work is intentionally unchanged. Dense multi-tile discovery also avoids the redundant
`ContainsKey` hashes for all but the first consecutive segment of each visible clothing entity.

No live in-game profiler measurement is claimed here. The generated client IL was verified to
contain one policy gate, empty-pass guards, and direct ID lookups. Dense-world frame-time comparison
remains the appropriate final validation.

## Safety and invalidation

The cache representation is **current-frame IDs**, not direct entity references. It is rebuilt by
vanilla discovery every solid draw, naturally handling placement, removal, destruction, world
change, area movement, and multiplayer tile-entity changes. A stale or wrong ID fails the typed
`TryGet<T>` check and is not drawn.

Further caching inside `TEHatRack.Draw` or `TEDisplayDoll.Draw` remains intentionally absent. Their
fake-player state, lighting, shader state, and native `PlayerRenderer` calls are frame-dependent.
Skipping individual non-empty hat-rack slots or merging the two immediate-mode batches was also
rejected: neither preserves the renderer's shader/state boundaries with enough confidence for a
version-locked visual optimization.
