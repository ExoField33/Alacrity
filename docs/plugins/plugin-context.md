# Plugin Context

`IPluginContext` is the sole plugin entry point. It supplies manifest metadata, logging, settings, storage, events, commands, keybinds, retained UI, HUD widgets, overlays, notifications, services, dispatcher, scheduler, user interaction, multiplayer, and safe Terraria snapshot services.

All registrations are activation-owned. Services either provide immutable snapshots safe for concurrent read or document a host main-thread/render-thread boundary. A plugin must not retain a context after disable.
