# Low-Level Plugins

Normal plugins use PluginSdk services and require no TerrariaIntegration change or executable patch. Version-sensitive hooks, new game capture paths, and binary rewrites are host-owned low-level work. They must be exact-version verified, fail closed, journaled, and expose a reusable framework-neutral SDK capability before any ordinary plugin depends on them.
