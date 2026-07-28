# Dependency Graph

Analysis snapshot: 2026-07-22. This graph describes the current implementation and the safe target direction. Arrows mean "depends on"; dotted dependencies are optional or runtime-discovered.

```text
Terraria.exe 1.4.5.6
  |-- Microsoft.Xna.Framework / Game / Graphics (GAC, runtime)
  |-- Terraria Content (runtime assets)
  |-- Windows user32.dll (foreground, focus, minimized state)
  |-- Windows Core Audio COM (default endpoint detection)
  `-- System.Windows.Forms (clipboard)

Program.ConsoleApp1
  |-- Mono.Cecil 0.11.6
  |-- Terraria.exe metadata
  |-- VanillaChatEnhancer.dll metadata
  `-- XNA/Terraria assembly resolution
       `-- writes Terraria.ChatEnhanced.exe

VanillaChatEnhancer.dll
  |-- EnhancerStorage
  |     |-- VanillaChatEnhancer.ini
  |     `-- VanillaChatEnhancerServers.ini
  |-- ChatInput
  |     `-- Terraria.Main input fields and keyboard state
  |-- Links
  |     |-- Terraria chat snippet APIs
  |     |-- System.Text.RegularExpressions
  |     |-- Windows shell
  |     `-- clipboard
  |-- ChatFeatures
  |     |-- chat text and player-name normalization
  |     `-- shared hidden-player state
  `-- Performance
        |-- Terraria.Main/IngameOptions/UIElement reflection
        |-- Player/NPC/Projectile/Dust/Gore/Item reflection
        |-- XNA SpriteBatch/Texture2D/Rectangle/Vector2
        |-- Net.Ping, Netplay, WorldSections, SoundEngine
        |-- tab-list and player-view UI
        |-- server browser and asynchronous ping
        |-- render gates/culling/draw-black optimization
        `-- startup, inactive-window, audio, and dash-sync hooks
```

## Proposed plugin/service graph

```text
Alacrity.App
  `-- Alacrity.Core
        |-- Alacrity.PluginSdk
        |-- Alacrity.Configuration
        |-- Alacrity.Diagnostics
        |-- Alacrity.PatchRuntime
        |     |-- TerrariaIntegration
        |     |-- signature/version validation
        |     |-- reversible patch transactions
        |     `-- lifecycle/shutdown ownership
        |-- Alacrity.TerrariaIntegration
        |     |-- immutable game snapshots
        |     |-- render/UI extension points
        |     `-- vanilla-compatible multiplayer session state
        `-- plugins
              |-- better-chat
              |-- chat-moderation-ui
              |-- tab-list
              |     `-- inspect-player
              |-- held-item-overlay
              |-- render-controls
              |-- diagnostics
              |-- ui-inspector
              |-- server-browser
              |-- inactive-window
              |-- audio-device
              `-- startup-performance [optional, benchmark-gated]

inspect-player -> tab-list
held-item-overlay -> tab-list/player visibility service
ui-inspector -> TerrariaIntegration UI observation service
all plugins -> PluginSdk/Core; no plugin -> raw Interop/Hooks
```

## Dependency classification

| Dependency | Type | Current state | Migration requirement |
|---|---|---|---|
| Terraria 1.4.5.6 internal types and methods | hard/versioned | Reflection plus Cecil name/signature lookup | Central version manifest and verified adapters; fail safe on mismatch. |
| XNA types | hard/runtime | Direct helper references | Isolate in Terraria integration/render service. |
| Mono.Cecil | hard/build-time | Patcher project package | Move behind a patch engine; keep generated output and transaction metadata separate. |
| INI files | shared state | Helper-owned beside DLL or fallback path | User-data directory migration with compatibility read and atomic writes. |
| Clipboard/shell | optional OS service | Direct WinForms/Process calls | Permission declaration, UI-thread rules, and failure-safe adapters. |
| asynchronous server ping | optional service | ThreadPool callback writes shared server data | Immutable result message or synchronized cache with lifecycle cancellation. |
| server policy/capability negotiation | absent | No implementation found | Future central multiplayer service; must remain optional and vanilla-compatible. |
