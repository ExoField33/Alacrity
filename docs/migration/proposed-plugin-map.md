# Proposed Plugin Map

This document explains the initial grouping. The editable machine-readable proposal is `migration/plugin-map.yaml`. Grouping is by coherent capability and lifecycle ownership, not by individual toggle.

## Grouping decisions

- `better-chat` owns text editing, links, and prefix transformation because all three alter local chat presentation/input and share chat hooks.
- `tab-list` owns the base player-list surface, cache, columns, sorting, ping, heads, and row controls.
- `inspect-player` is separate and depends on `tab-list`; it owns selection, the detailed player view, snapshots, item slots, and preview rendering.
- `render-controls` groups dust, gore, damage-number, culling, darkness, and tile-render controls. Their toggles remain independent, but their lifecycle and SpriteBatch failure handling are shared.
- `diagnostics` owns overlays that expose state without changing it: hitboxes, NPC targets, tile sections, and item-drop labels.
- `chat-moderation-ui` is a presentation-only name for local visibility filters. It must not be marketed or implemented as bot detection, anti-cheat, or security enforcement.
- `server-browser` is separate because it has persistent server data, text-entry lifecycle, asynchronous ping, and reconnect state.
- `patch-runtime`/`TerrariaIntegration` are platform services, not ordinary user plugins. They own version checks, snapshots, reflection metadata, patch transactions, and shutdown.
- `multiplayer-compatibility` is intentionally marked restricted. The dash dirty-flag patch should not be migrated into a generally permitted plugin until packet and policy review is complete.

## Ambiguities to resolve before implementation

- Whether hidden-player and hidden-projectile controls should remain together or split into a generic local-visibility service plus tab-list UI.
- Whether item-drop labels belong under `diagnostics` or a separate accessibility plugin. Current proposal keeps them with diagnostics because they are a world-state overlay.
- Whether `inactive-window`, `audio-device`, and startup behavior are core services rather than plugins. The YAML keeps them as optional plugins until lifecycle ownership is designed.
