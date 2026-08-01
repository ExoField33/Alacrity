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
| `context.Notifications` | Bounded, targeted, colored transient notifications. |
| `context.Services` | Dependency-aware cross-plugin service publication and lookup. |
| `context.Settings` | Typed persisted settings, validation, migration, subscriptions, and reset. |
| `context.Storage` | Path-confined per-plugin persistent data. |
| `context.Events` | Typed, ordered, scope-owned event subscriptions. |
| `context.Commands` | Scope-owned command registration and dispatch. |
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
| `context.Terraria.Chat` | Input editors, message decorators/filters, and link handlers. |
| `context.Terraria.Entities` | Shared immutable active entity snapshots; plugins may request scoped, demand-gated melee collision snapshots through `IPluginMeleeCollisionSnapshotService`. |
| `context.Terraria.Players` | Detached player name, team, life, death/ghost status, buffs, and host-derived suspected-bot state. |
| `context.Terraria.Session` | Server/world display name, capacity, and a bounded sampled ping value. |
| `context.Terraria.VisualEffects` | Scoped dust/gore presentation policies. |

## Presentation rules

- Use `context.Hud` for retained gameplay UI such as a player list.
- Use `context.Overlays` for world/HUD/menu drawing that does not require interactive retained UI.
- Use `context.Ui` for settings and menu contributions, including reusable hover/click icon behavior.
- Player/NPC avatars, texture lookup, pointer capture, SpriteBatch state, and camera details remain
  host-owned TerrariaIntegration responsibilities.

## Extension guidance

Prefer extending the closest existing service when the lifetime, permission, and rendering model are
already the same. Create a new public service only when a capability has a distinct ownership,
thread-affinity, or security boundary. Ordinary plugins must not require a plugin-named bridge method
or a new Terraria executable patch; reusable integration capabilities belong in the host instead.
