# Permissions

`plugin.json` declares capabilities and permissions before an assembly loads. Core and the Terraria adapter enforce these declarations at each service boundary. For example, entity snapshots require `GameStateRead` and `ReadGameState`; session snapshots require multiplayer observation declarations; clipboard and external-link operations require their corresponding user-interaction permissions.

Permissions mediate host services. They are not a sandbox for arbitrary managed code on the current runtime.
