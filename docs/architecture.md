# Alacrity Architecture

Alacrity has four boundaries: `Alacrity.PluginSdk` holds framework-neutral public contracts; `Alacrity.Core` owns lifecycle, permissions, registries, persistence, and cleanup; `Alacrity.TerrariaIntegration` adapts verified Terraria 1.4.5.6 hooks; and `Alacrity.App` owns presentation models. Plugins receive only `IPluginContext`, never a global runtime or raw Terraria/XNA objects.

The integration creates one internal `ITerrariaClientRuntime`, grouped into lifecycle, game state, rendering, communication, plugin UI, and visual-effects services. The patched static bridge is an ABI facade over that runtime, not a plugin-specific implementation layer.
