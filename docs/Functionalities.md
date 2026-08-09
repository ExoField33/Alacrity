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
| `context.Storage` | Path-confined per-plugin persistent data. |
| `context.Events` | Typed, ordered, scope-owned event subscriptions. |
| `context.Commands` | Scope-owned explicit command registration and optional fluent typed command binding with aliases, normalized help metadata, validation, and quoted arguments. |
| `context.Keybinds` | Native Terraria controls-menu keybind registration and runtime dispatch. |
| `context.Ui` | Retained settings pages/controls and reusable icon interactions. |
| `context.Overlays` | Bounded world, HUD, and menu overlay callbacks without raw XNA access. |
| `context.Hud` | Retained gameplay HUD widgets with panels, text, approved assets, avatars, and owned icon actions. |
| `context.UserInteraction` | Permission-gated clipboard and external HTTP/HTTPS link actions. |
| `context.Terraria` | Framework-neutral Terraria integration services. |
| `context.Multiplayer` | Read-only connection and server-policy state. |

## Terraria services

| Service | Current purpose |
| --- | --- |
| `context.Terraria.Chat` | Input editors, message decorators/filters, and link handlers. Editors can optionally claim normalized native actions such as Up/Down or scroll and request a host-owned visible-chat offset without receiving raw Terraria input or chat-monitor objects. |
| `context.Terraria.Entities` | Shared immutable active entity snapshots with counts, caller-buffer copying, slot lookup, generation-aware handle lookup, and optional scoped melee capture demand. |
| `context.Terraria.Players` | Detached player name, team, life, death/ghost status, buffs, and host-derived suspected-bot state, with caller-buffer copying and generation-aware lookup. |
| `context.Terraria.Session` | Server/world display name, capacity, and a bounded sampled ping value. |
| `context.Terraria.VisualEffects` | Scoped dust/gore presentation policies. |
| `context.Terraria.RenderCulling` | Scoped conservative local culling requests for verified world player, dropped-item, Dust, and common world-particle draw paths. It requires the generic `Rendering` capability, not raw renderer or UI access. |
| `context.Terraria.NpcTargets` | Demand-gated immutable hostile NPC-to-player targeting relationships for local presentation diagnostics. |
| `context.Terraria.WorldSections` | Bounded immutable visible client tile-section state captured at the host update boundary; callers choose a small section margin rather than reading the full section grid. The first read activates capture for that activation and may return an empty/previous frame until the next update. |

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

## Extension guidance

Prefer extending the closest existing service when the lifetime, permission, and rendering model are
already the same. Create a new public service only when a capability has a distinct ownership,
thread-affinity, or security boundary. Ordinary plugins must not require a plugin-named bridge method
or a new Terraria executable patch; reusable integration capabilities belong in the host instead.
