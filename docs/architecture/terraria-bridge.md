# Terraria Bridge

`PluginUiRuntime` is the version-locked static ABI referenced by patched Terraria 1.4.5.6. Its stable entry points bootstrap the grouped internal runtime and forward into generic chat, input, rendering, visual-effects, and lifecycle adapters. Compatibility forwarding names remain only for the currently patched executable; new plugin-specific bridge entry points are not added.
