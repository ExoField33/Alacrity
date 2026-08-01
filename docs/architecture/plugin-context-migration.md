# Plugin Context Migration

## Current baseline

`IPluginContext` is already the public facade for manifest identity, scoped resources, logging,
settings, storage, events, commands, keybinds, UI contributions, overlays, user interaction,
Terraria chat, cross-plugin services, and multiplayer state. `Alacrity.Core` creates and owns
these services through `PluginHostContextFactory`; registrations are attached to one
`IPluginResourceScope` and are removed on disable, fault cleanup, or shutdown.

The public SDK remains framework-neutral. Terraria, XNA, SpriteBatch, and mutable entities are
confined to `Alacrity.TerrariaIntegration`.

## Remaining plugin-specific integration

- `DrawPlayerList` plus `IPlayerListService` remains a compatibility renderer while a general HUD-widget
  contract is introduced. The bridge no longer accepts it by hard-coded package ID: it validates the
  publishing manifest instead.

The remaining entries are compatibility adapters, not the target extension model. They remain
only while the matching reusable capabilities are introduced and the bundled plugins are migrated.

Hitboxes now uses `context.Terraria.Entities` and `context.Overlays` directly with a `World` overlay descriptor. Its former named
renderer is a version-locked forwarding hook only; it dispatches the generic world-overlay host
and contains no Hitboxes settings, service lookup, or drawing logic. The melee rectangle capture
remains a core integration capability because Terraria computes it inside a version-sensitive
combat method.

## Reusable integration capabilities

- Overlay registrations declare one explicit phase: `World`, `Hud`, or `Menu`. The host caches an
  immutable ordered snapshot per phase and rebuilds it only when a registration changes.
- The Terraria bridge resolves the plugin manager, notifications, keybind controls, visual-effect
  gates, world/menu overlays, combat-collision capture, and commands independently. A missing
  optional UI method therefore leaves unrelated runtime capabilities available.
- Chat decorators are deterministic and composable. Existing decorators remain compatible; a
  decorator that needs to preserve existing link spans can implement `IChatSpanDecorator`.
- `TerrariaEntitySnapshotCache` captures detached entity data once per game tick and serves all
  authorized plugins from shared reusable buffers. Its melee-collision capture demand is zero-cost
  at the injected hook while no plugin requests it.

## Incremental target

1. Add typed settings and direct retained-control bindings while preserving current keys and JSON files.
2. Add reusable read-only Terraria snapshots/events and a concrete Terraria adapter with cached
   buffers. No SDK type exposes Terraria objects.
3. Retain and dispatch the existing generic `PluginOverlayHost` through a Terraria-owned canvas;
   migrate HUD presentation after visual parity tests exist.
4. Replace the Player List compatibility service with a general HUD-widget contract, then delete
   its compatibility renderer.

Every stage preserves package IDs, persisted setting keys, capabilities, lifecycle cleanup, and
vanilla fallback behavior.

## Capability status

| Capability | Status | Migration direction |
| --- | --- | --- |
| Settings, storage, logging | Context service; typed settings and scope-owned subscriptions | Active migration |
| Keybinds, commands, UI settings | Context service with scope ownership | Complete |
| Notifications | Context service with color, target, and bounded lifetime | Complete |
| Chat | `context.Terraria.Chat`; host owns hooks and tag rendering | Complete compatibility surface |
| Generic overlays | Context service plus Terraria-owned canvas with distinct world/HUD/menu phases | Hitboxes migrated to `World`; per-plugin logging, immutable draw-frame data, and cached phase snapshots active |
| Entity snapshots | `context.Terraria.Entities` with caller-reused buffers and detached values | Hitboxes migrated; Player List can consume the same read-only source later |
| Visual-effect policy | `context.Terraria.VisualEffects` scoped policy registry | Dust & Gore Toggle migrated |
| Sound | Missing | Add a narrow host-owned sound request service only when a plugin needs it |
