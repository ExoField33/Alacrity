# Migration Risks

Analysis snapshot: 2026-07-22. Ratings reflect the current evidence and the Alacrity security/compatibility requirements. These are migration risks, not claims that every item is currently broken.

| Priority | Risk | Why it matters | Required mitigation before migration |
|---|---|---|---|
| Blocker | No reversible patch transaction or verified original backup | The patcher writes `Terraria.ChatEnhanced.exe` but does not record ownership, restore the original, or validate a rollback. | Add version/hash inventory, verified backup, per-patch transaction log, uninstall restoration, and launch-state verification. |
| Blocker | Dash-keybind patch changes multiplayer dirty-state/send timing | This crosses the stated packet/gameplay boundary and can create desync or server-specific behavior. | Keep out of ordinary migration; require explicit compatibility/security review, packet characterization, and server-authoritative invariants. |
| Blocker | No plugin trust, signature, integrity, or server-policy enforcement layer | Arbitrary DLL/plugin loading would create a security boundary without verification. | Establish signed manifests, package hashes, capability declarations, policy validation, safe defaults, and fail-closed enforcement for protected sessions. Never add bypasses. |
| High | Large monolithic helper with static global state | Extraction can change initialization order, shared caches, settings, and draw/input ordering. | Build characterization tests first; introduce shared services and adapters incrementally. |
| High | Reflection and method lookup are version-fragile | Internal fields, methods, nested types, and parameter order are assumed from 1.4.5.6. | Centralize verified metadata; cache it; gate by exact assembly identity/hash; disable only the affected feature on mismatch. |
| High | UI and rendering hooks run on hot paths | UIElement observation, entity culling, tab list, tooltips, and labels can allocate or invoke reflection repeatedly. | Add disabled fast paths, allocation benchmarks, frame budgets, immutable snapshots, and no per-frame file/network work. |
| High | Render-only versus simulation behavior is easy to conflate | Dust/gore, hidden players, culling, and damage numbers must not change gameplay simulation or server state. | Separate render gates from update gates; tests must prove simulation continues and only pixels change. |
| High | Player-view extraction and full preview mutate/read live Terraria objects | Reflection-driven inventory/equipment access and preview lighting can leak state or alter rendering state. | Capture read-only snapshots on view-open; restore all SpriteBatch/lighting state in `finally`; never expose writable game references to plugins. |
| High | Server ping worker has no formal synchronization contract | ThreadPool callbacks can race UI reads, server removal, renaming, and menu teardown. | Use immutable results keyed by stable server ID, synchronized publication, cancellation, and no worker access to disposed UI state. |
| High | Server browser text/input/menu modes are custom global integers | `777013` and `777014` can collide with future Terraria versions or leave stale reconnect/editor state. | Replace magic modes with a versioned UI/session service and cleanup on every exit path; preserve vanilla Escape behavior. |
| High | Installation paths and settings beside the DLL | Read-only game directories can silently fail, and uninstall cannot distinguish user data from managed files. | Use `%AppData%`/documented Alacrity data root, compatibility-read old INIs, atomic writes, and explicit preservation/deletion manifests. |
| Medium | Startup thread-pool and forced memory trim may hurt startup/stutter | Raising minimum workers, forced Gen2 collection, finalizer waits, or `EmptyWorkingSet` can increase context switching/page faults. | Treat as optional benchmark-gated experiments; default off until measured on representative hardware. |
| Medium | Windows-only interop | user32, WinForms clipboard, Core Audio COM, and process APIs prevent portability and complicate shutdown. | Isolate in `Client.Interop`/platform services; handle unavailable APIs with original behavior. |
| Medium | URL opening is an external side effect | A chat link can launch an arbitrary user-approved shell target. | Normalize/validate schemes, make opening explicit, avoid automatic navigation, and keep clipboard-only fallback. |
| Medium | Heuristic bot and hostile-NPC detection | Names/equipment/net IDs are not authoritative identity or hostility signals. | Label as best-effort presentation only; never use for enforcement, moderation, or security decisions. |
| Medium | Asset loading and texture lifetime are implicit | Texture fields are discovered lazily and may be unavailable during startup or menu transitions. | Ownership registry, lazy feature-local asset loading, disposal/shutdown rules, and retryable nonfatal failure. |
| Medium | `DrawBlack` and special-tile optimizations have incomplete visual evidence | Darkness/tile changes can appear only in specific biomes, slopes, liquids, invisible blocks, zoom, or world edges. | Snapshot matrix across those cases; keep toggleable and rollback to vanilla on any mismatch. |
| Low | Empty catches hide diagnostics | Feature failures can silently degrade and make support difficult. | Add a rate-limited diagnostic sink with privacy-safe context; never log chat contents or credentials. |
| Low | Generated binaries/INI files obscure source ownership | Root artifacts can be mistaken for authoritative source and overwritten during builds. | Document generated outputs and add clean artifact directories after migration. |

## Migration sequencing recommendation

1. Freeze behavior with exact-version characterization tests and a patch/signature manifest.
2. Extract shared configuration, diagnostics, immutable snapshots, and patch ownership services.
3. Migrate the lowest-risk client-only feature first, likely link styling or render settings, with rollback.
4. Migrate `tab-list` and `inspect-player` as a dependency pair only after cache/allocation tests exist.
5. Migrate server browser and asynchronous ping after lifecycle synchronization is implemented.
6. Leave dash synchronization, startup memory trim, and any unverified render-black optimization behind explicit review gates.

No migration or implementation was performed in this analysis phase.
