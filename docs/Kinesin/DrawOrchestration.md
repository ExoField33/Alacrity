# Draw Orchestration

Kinesin's **Optimize Draw Orchestration** setting is enabled by default. It is a host-owned,
version-locked optimization request; the plugin does not receive `Main`, `SpriteBatch`, or any
other Terraria rendering object.

The native `Main.DoDraw` path performs two immediate lighting passes on `renderNow` frames. The
passes remain separate because both are required, but their identical camera-derived rectangle is
reused for the second call. Kinesin also avoids the transient `List` allocations in the two
special projectile-cache sorts when their unique projectile type is absent.

Kinesin also inspected the surface-background desert transition helper. Terraria already caches its
compiler-generated `Func<int, bool>` in a static field, so it does not allocate after first use;
the helper is intentionally left untouched.

Terraria's surface and background renderers already run inside compatible deferred SpriteBatch
passes. Kinesin deliberately does not merge their individual draws: their parallax, alpha
transitions, layer ordering, and per-background state are observable rendering behavior. The
painted-tile optimization is different because it deduplicates redundant render-target requests,
not visible tile/background draws.
