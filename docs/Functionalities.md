# Alacrity SDK Functionalities

This is the maintained capability inventory for plugin work. Before adding a public API, check
whether an existing context service can be extended without duplicating a host boundary.

## Context services

| Context member | Current purpose |
| --- | --- |
| `context.Manifest` | Verified package metadata from `plugin.json`. |
| `context.Resources` | Scope-owned cleanup in reverse registration order, including child scopes. |
| `context.Logger` | Plugin-attributed diagnostics. |
| `context.Dispatcher` | Scope-owned main-thread work dispatch. |
| `context.Scheduler` | Scope-owned next-update, interval, elapsed-time, and named background work. Background work is bounded per activation, cancelled at teardown, and observed without blocking Terraria's update/render thread. |
| `context.Notifications` | Bounded, targeted, colored transient notifications. |
| `context.Services` | Dependency-aware cross-plugin service publication and lookup. |
| `context.Settings` | Typed persisted settings, validation, migration, subscriptions, and reset. |
| `context.Ui.RegisterSettingsControl(...)` | Host-rendered activation-scoped toggles, cycles, searchable dropdowns, sliders, and color controls. Dropdown options are immutable labelled values; a dynamic option provider may publish a refreshed list while the host owns local text filtering, word/caret editing, scrolling, navigation, input, sounds, and Escape behavior. |
| `context.Storage` | Path-confined per-plugin persistent data. |
| `context.Events` | Typed, ordered, scope-owned event subscriptions. |
| `context.Commands` | Scope-owned explicit command registration and optional fluent typed command binding with aliases, normalized help metadata, validation, and quoted arguments. |
| `context.Keybinds` | Native Terraria controls-menu keybind registration and runtime dispatch. |
| `context.Ui` | Retained settings pages/controls and reusable icon interactions. |
| `context.Overlays` | Bounded world, HUD, and menu overlay callbacks without raw XNA access. |
| `context.Hud` | Retained gameplay HUD widgets with panels, text, approved assets, avatars, and owned icon actions. |
| `context.UserInteraction` | Permission-gated clipboard and external HTTP/HTTPS link actions. |
| `context.Network` | Permission-gated, bounded HTTPS requests to exact DNS hosts declared in the verified plugin manifest. Requests remain activation-scoped and should run through `context.Scheduler.RunBackground(...)`; plugins never receive `HttpClient`, sockets, API credentials, or mutable Terraria state. |
| `context.Terraria` | Framework-neutral Terraria integration services. |
| `context.Multiplayer` | Read-only connection and server-policy state. |

## Terraria services

| Service | Current purpose |
| --- | --- |
| `context.Terraria.Chat` | Input editors, message decorators/filters, link handlers, owner-scoped interactive message actions, host-rendered chat action buttons/popovers, and asynchronous outgoing-message transformers. Editors can optionally claim normalized native actions such as Up/Down or scroll and request a host-owned, bounded visible-chat offset without receiving raw Terraria input or chat-monitor objects. |
| `context.Terraria.Entities` | Shared immutable active entity snapshots with counts, caller-buffer copying, slot lookup, generation-aware handle lookup, and optional scoped melee capture demand. |
| `context.Terraria.Players` | Detached player name, team, life, death/ghost status, buffs, and host-derived suspected-bot state, with caller-buffer copying and generation-aware lookup. |
| `context.Terraria.Session` | Server/world display name, capacity, and a bounded sampled ping value. |
| `context.Terraria.VisualEffects` | Scoped dust/gore presentation policies. |
| `context.Terraria.RenderCulling` | Scoped conservative local culling requests for verified world player, dropped-item, Dust, and common world-particle draw paths. It requires the generic `Rendering` capability, not raw renderer or UI access. |
| `context.Terraria.RenderingOptimizations` | Scoped requests for verified, host-owned local renderer preparation/presentation optimizations, including painted-tile preparation, clothing-entity rendering, waterfall presentation, TileDrawing common-path, draw-orchestration reductions, batched laser-ruler and rain presentation, conservative static 20-by-20 tile descriptors with live lighting, and balanced vanilla-lighting parallel ranges. Plugins select documented policy categories; they never receive raw renderer, texture, or graphics-device access. |
| `context.Terraria.Presentation` | Scoped requests to suppress specific host-supported local presentation elements. Policies compose by element union, require the generic `Rendering` capability, and never expose renderer state. |
| `context.Terraria.NpcTargets` | Demand-gated immutable hostile NPC-to-player targeting relationships for local presentation diagnostics. |
| `context.Terraria.WorldSections` | Bounded immutable visible client tile-section state captured at the host update boundary; callers choose a margin from `0` through `PluginWorldSectionLimits.MaximumMargin` (currently `8`) rather than reading the full section grid. The first read activates capture for that activation and may return an empty/previous frame until the next update. |

## Native Text Input

`Main.GetInputText(...)` is a version-locked core-client seam, not a PluginSdk service. Alacrity
uses it for all focused native desktop text fields, including player chat, signs, chest names,
server fields, and Terraria search boxes. The core editor provides caret movement, Shift
selection, Home/End, Ctrl word movement/deletion, atomic Terraria item/glyph tags, surrogate-pair
movement, and standard clipboard shortcuts. It falls back to Terraria's original method whenever
the client is unfocused or the bridge declines the input frame.

BetterChat remains responsible only for plugin behavior that is genuinely chat-specific, such as
history, visible-chat scrolling, and host-owned chat action menus. It does not own ordinary text
editing for other Terraria interfaces.

## Presentation rules

- Use `context.Hud` for retained gameplay UI such as a player list.
- Use `context.Overlays` for world/HUD/menu drawing that does not require interactive retained UI.
- Use `context.Ui` for settings and menu contributions, including reusable hover/click icon behavior.
- Player/NPC avatars, texture lookup, pointer capture, SpriteBatch state, and camera details remain
  host-owned TerrariaIntegration responsibilities.
- Use `context.Terraria.NpcTargets` with a world overlay for reusable threat/target diagnostics.
  It never exposes live NPCs or players.
- Use `context.Terraria.WorldSections` with a world overlay for visible-region loading diagnostics.
  It is read-only, activation-scoped, thread-safe to copy, and does not expose Terraria's mutable
  network section matrix or read live `Main` state from plugin callbacks.
- Use `context.Terraria.RenderCulling` only for local presentation policies. The host preserves
  partially visible content, accounts for the current camera scale and resolution, and fails open
  for renderer types without a verified world position. Terraria already camera-bounds tile/vine
  drawing and projectile rendering, so plugins should not duplicate those paths.
- Use `context.Terraria.RenderingOptimizations` only for explicitly documented, version-verified
  optimization categories. Policies compose by category union and fail closed to unchanged vanilla
  work when the integration bridge is unavailable.
- Use `context.Terraria.Presentation` for deliberately narrow local presentation choices such as
  hiding a supported endpoint indicator. This is not gameplay access: unsupported elements and a
  missing bridge always leave Terraria's native presentation intact.
- Use `context.Terraria.Chat.RegisterActionButton(...)` for a package asset placed in the
  host-owned chat action strip. The button receives normalized left/right click and Shift-click actions and menu
  rows, including immutable nested choice rows, an explicit upward/downward child-chooser direction,
  and optional host-rendered background accents; it does not receive SpriteBatch, pointer capture,
  or live chat objects. The host owns popover scrolling, local text filtering, Escape-back
  navigation, sounds, and the input-height layout. Interactive
  message text belongs to the producing plugin through `RegisterMessageAction(...)`, so one
  plugin cannot handle another plugin's span.
- Use `context.Terraria.Chat.RegisterOutgoingMessageTransformer(...)` only for asynchronous,
  non-command text preparation. The first priority-ordered transformer that claims input owns it;
  the host keeps the chat open while it runs, then performs exactly one normal Terraria submission.
  A failed transform leaves the original input unsent rather than forwarding an unreviewed value.
- Remote APIs use `context.Network` with the `Networking` capability, `NetworkAccess` permission,
  and exact `networkHosts` entries in `plugin.json`. Do not place personal or unrestricted API keys
  in manifests or source; a package-owned credential intended for public distribution must be
  restricted to its exact declared host and endpoint.

## Extension guidance

Prefer extending the closest existing service when the lifetime, permission, and rendering model are
already the same. Create a new public service only when a capability has a distinct ownership,
thread-affinity, or security boundary. Ordinary plugins must not require a plugin-named bridge method
or a new Terraria executable patch; reusable integration capabilities belong in the host instead.
