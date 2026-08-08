# Compatibility

Packages declare exact PluginSdk, Core host, and Terraria bridge ABI compatibility versions in `plugin.json`, alongside supported Terraria versions. Admission occurs before the entry assembly loads. A mismatch reports the stale component and expected versus loaded version rather than relying on a CLR missing-member failure. Current bundled packages require compatibility level `2`.

The injected bridge retains its stable `GetBridgeHandshake(): string` ABI for version-locked
Terraria patches. Its `PluginSdk|Host|BridgeAbi|Terraria` payload is parsed by a single typed
compatibility descriptor. Malformed handshakes and mixed managed assemblies therefore identify
each stale component before the optional plugin runtime starts, while Terraria continues with its
vanilla path.
