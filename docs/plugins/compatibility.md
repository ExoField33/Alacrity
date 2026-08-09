# Compatibility

Packages declare exact PluginSdk, Core host, and Terraria bridge ABI compatibility versions in `plugin.json`, alongside supported Terraria versions. Admission occurs before the entry assembly loads. A mismatch reports the stale component and expected versus loaded version rather than relying on a CLR missing-member failure. Current bundled packages require compatibility level `2`.

The injected bridge retains its stable `GetBridgeHandshake(): string` ABI for version-locked
Terraria patches. Its `PluginSdk|Host|BridgeAbi|Terraria` payload is parsed by a single typed
compatibility descriptor. Malformed handshakes and mixed managed assemblies therefore identify
each stale component before the optional plugin runtime starts, while Terraria continues with its
vanilla path.

For a generated client, the patch-facing facade also validates `alacrity-client-manifest.json`
once before loading `Alacrity.PluginUiCoreBridge.dll`. The final client manifest verifies the
patched executable and deployed runtime-file hashes; `runtime-manifest.txt` remains a build-stage
record only. Missing, malformed, unsafe, or mismatched generated-client manifests disable the
optional Alacrity runtime with a diagnostic and leave Terraria on its vanilla path.
