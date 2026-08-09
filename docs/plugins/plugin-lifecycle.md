# Plugin Lifecycle

Packages progress through discovery, validation, loading, fault/restart-required handling, and uninstall. Activations independently progress through initialization, enable, disable, and shutdown. Every activation receives a fresh `IPluginContext` and resource scope. Disabling releases resources in reverse registration order; old services reject use and cannot register into a later activation.

Dependencies enable in dependency-first order and cleanup rolls back in reverse order. Sync and async callbacks share the same controller; async work has cancellation and bounded shutdown handling.

Teardown first closes activation-wide callback admission. Commands, events, keybinds, chat handlers,
HUD/widgets, overlays, scheduled work, dispatcher work, and icon actions that have not yet started
are rejected for the retiring activation, while callbacks already holding a lease may finish. This
does not block Terraria's update or draw threads. Timed-out asynchronous lifecycle callbacks
quarantine their plugin instance so no later lifecycle callback can run concurrently with retained
plugin code; the host observes any late failure for diagnostics.
