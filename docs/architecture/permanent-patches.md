# Permanent Terraria Patches

`Alacrity.ClientBuilder` owns the only supported permanent patch path. A **patch** changes the
clean, hash-verified Terraria executable at build time. A **bridge method** is a stable
`AlacrityTerraria.PluginUiRuntime` ABI call injected by a patch. Runtime behavior remains in the
Terraria integration and Core; ordinary plugins never receive patching or raw Terraria access.

```text
clean Terraria 1.4.5.6 + exact SHA-256
    -> permanent patch catalog
    -> PluginUiRuntime ABI calls
    -> TerrariaIntegration runtime subsystems
    -> scoped PluginSdk services
```

## Supported catalog

The current catalog is `alacrity-terraria-1.4.5.6-r2`. It is intentionally version locked to the
single Steam Windows Terraria 1.4.5.6 SHA-256 recorded in `SupportedTerrariaBuildCatalog`.
Unknown binaries can be inspected, but cannot be generated into a normal Alacrity client.

The six independently applied catalog definitions are the authoritative inventory in
`tools/Alacrity.ClientBuilder/PermanentPatchCatalog.cs`. Each enumerates every exact target member,
verified anchor, injection style, and bridge ABI postcondition. The source hash makes the exact
target method bodies part of each definition's precondition; the builder still validates the staged
bridge handshake, reopens the resulting executable, and verifies every injected facade method
reference.

| Area | Terraria target(s) | Bridge ABI responsibility | Runtime subsystem |
| --- | --- | --- | --- |
| Startup/menu | `Terraria.Main`, menu UI paths | bootstrap, plugin manager, version drawing | runtime/bootstrap and plugin UI |
| Settings/input | `Terraria.IngameOptions`, `UIManageControls`, input/update paths | in-game settings, keybind state/controls, input | UI and input |
| Rendering | `Terraria.Main` draw paths | notifications and world overlays | notifications and rendering |
| Combat | melee collision calculation | collision bounds capture | combat presentation |
| Visual effects | dust/gore creation, update, and draw paths | dust/gore policy gates | visual-effects policy registry |
| Chat | chat input, draw, parse, snippets, visibility paths | generic input editor, command, decorator, link, and visibility dispatch | chat |

The builder rejects a source that already contains an `AlacrityTerraria.PluginUiRuntime` call. This
prevents duplicate permanent injection. It does not treat a patched executable as a fresh source.

## Adding a supported game version

1. Audit the clean executable and add its exact version, distribution identity, SHA-256, and patch
   catalog identity to `SupportedTerrariaBuildCatalog`.
2. Add strict target/signature/anchor checks for every changed transformation. Never use the first
   matching method or fuzzy version checks.
3. Stage the matching facade and Core bridge together, then extend ABI and manifest tests.
4. Generate to `artifacts/client`, reopen it with Cecil, validate injected ABI calls, and run the
   client manually before enabling deployment.

Permanent patches are not activation-scoped patches. The existing host-owned file patch system is
separate and retains its journals, backups, rollback, and ownership model.
