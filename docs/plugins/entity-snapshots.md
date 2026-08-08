# Entity Snapshots

`context.Terraria.Entities` and `context.Terraria.Players` expose detached snapshots from the shared update-phase cache. Creating a service does not trigger capture; capture demand is acquired lazily by data use. Reuse caller-owned collections for copies.

`PluginEntityHandle` includes kind, native slot, and generation. A slot that becomes inactive then active receives a new generation, so `TryGetByHandle` cannot resolve a replacement entity as the old one. Plugins never receive a live Terraria entity.
