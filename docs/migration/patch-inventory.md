# Patch Inventory

Analysis snapshot: 2026-07-22. Every patch below is installed by `Program/ConsoleApp1/Program.cs` into a copy named `Terraria.ChatEnhanced.exe`. The patcher rejects assemblies whose version is not exactly `1.4.5.6` and imports methods from the helper DLL beside the target executable.

| ID | Terraria target | Current patch action | Proposed owner | Risk |
|---|---|---|---|---|
| P01 | `Terraria.Program` startup path | Calls `StartupTweaks.ApplyEarly` | `startup-performance` / shared runtime | Medium: startup ordering and thread-pool behavior. |
| P02 | `Terraria.Main.GetInputText` | Routes text through `ChatInput.Process` | `better-chat` | High: input signature/caret behavior; must preserve original fallback. |
| P03 | `Terraria.Main.DoUpdate_HandleChat` | Applies outgoing chat prefix | `better-chat` | Medium: message submission timing and command handling. |
| P04 | `Terraria.Main.DrawPlayerChat` | Uses `ChatInput.WithCursor` | `better-chat` | Medium: draw-time string/caret allocations and blinking stability. |
| P05 | `Terraria.UI.Chat.TextSnippet.OnClick` | Opens recognized URLs | `better-chat` | Medium: OS shell invocation and untrusted URL handling. |
| P06 | `Terraria.UI.Chat.TextSnippet.OnHover` | Shows link hover state/tooling | `better-chat` | Low/medium: UI callback behavior. |
| P07 | `Terraria.UI.Chat.TextSnippet.GetVisibleColor` | Changes link/message color | `better-chat` | Low: presentation only. |
| P08 | `Terraria.UI.Chat.ChatManager.ParseMessage` | Replaces/extends message snippets with linkified snippets | `better-chat` | High: parser compatibility and message formatting. |
| P09 | `Terraria.Main.DrawDust` | Early render gate | `render-controls` | Medium: all dust draw variants must be gated before SpriteBatch state changes. |
| P10 | `Terraria.Main.DrawGore`, `DrawGoreBehind`, `DrawBackGore` | Early gore render gates | `render-controls` | Medium: visual ordering and draw-state preservation. |
| P11 | `Terraria.Dust.NewDust`, `UpdateDust` | Creation/update gates and exception checks | `render-controls` | High if simulation is accidentally changed; current linked toggle must be characterized. |
| P12 | `Terraria.Gore.NewGore`, `Update` | Creation/update gates | `render-controls` | Medium/high: gore simulation and spawn side effects. |
| P13 | `Terraria.Main.DoDraw` combat-text loop and server-popup path | Gates combat text instances and server popups | `render-controls` | High: distinguishing client damage numbers from server status text. |
| P14 | `Terraria.IngameOptions.Draw` | Inserts enhancer settings UI | `render-controls` / `ui-inspector` / shared UI | High: vanilla settings lifecycle, scrolling, input, and category placement. |
| P15 | `Terraria.UI.UIElement.Draw` | Calls `Performance.ObserveDrawnUiElement` | `ui-inspector` | High: per-element draw hot path; must be no-op when disabled and allocation-free in steady state. |
| P16 | `Terraria.Main.Draw`/draw overlay path | Inserts player-list overlay | `tab-list` | High: input ownership, hold/release behavior, draw order, and stale runtime state. |
| P17 | `Terraria.Main` menu/update path | Inserts server-browser preparation and drawing | `server-browser` | High: menu modes, text input, mouse ownership, focus, and reconnect state. |
| P18 | Terraria emote/projectile draw layer | Adds held-item emote-like overlay and hidden-player emote guard | `held-item-overlay` / `chat-moderation-ui` | Medium/high: ordering with vanilla emotes and player visibility. |
| P19 | Player preview/fullbright rendering | Calls `ShouldForcePlayerPreviewFullbright` around preview draw | `inspect-player` | Medium: lighting/color state must be restored exactly. |
| P20 | World-item and dust instance draw methods | Calls screen-bounds gates | `render-controls` | Medium/high: camera/zoom/resolution and partial visibility correctness. |
| P21 | Tile drawing (`DrawGrass`, `DrawVines`) | Calls special-tile screen-bounds gates | `render-controls` | Medium: tile coordinate and section boundary assumptions. |
| P22 | `Terraria.Main.DrawBlack` | Optional optimized replacement/short path | `render-controls` | High: visual correctness across lighting modes and world boundaries. |
| P23 | Player/projectile/close-player overlays | Guards player, projectile, pet, health-bar, mouse-over and nearby-player overlay rendering | `chat-moderation-ui` | High: hiding must remain presentation-only and not remove simulation/network updates. |
| P24 | `Terraria.Main.DoUpdate_HandleInput` | Returns early when window is not focused | `inactive-window` | High: input/thread affinity and first-startup behavior. |
| P25 | `Terraria.Main.DoDraw` | Calls inactive-frame throttle | `inactive-window` | High: must not block the game thread or create visible focus transitions. |
| P26 | `Terraria.Main.UpdateAudio` | Calls audio-device change check | `audio-device` | Medium/high: COM interop and audio resource lifetime. |
| P27 | `Terraria.Player.Update` | Sets the verified player-control dirty flag for dash-keybind synchronization | `multiplayer-compatibility` | Very high/policy-sensitive: changes multiplayer packet timing and is outside ordinary client-only presentation scope. |

## Patch ownership observations

- There is no centralized patch registry, transaction, uninstall record, signature manifest, or original-file backup in the current patcher.
- Patches are installed in one fixed sequence and failures surface as a patch-time exception; there is no per-patch rollback transaction.
- Most patches call static helper methods by imported assembly reference. The helper is loaded from a DLL beside Terraria and is not package-validated by the patcher.
- P15, P16, P17, P20, P22, and P23 are render/UI hot paths. They require allocation and frame-time characterization before extraction.
