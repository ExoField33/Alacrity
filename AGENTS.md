This is a folder/repository containing the code for a Terraria utility client, Alacrity. The client's main goal is to be an improvement over the base game while ensuring game behavior stays the same. The main goals are: optimizations/performance improvements, modernize the client, multiplayer specific qol features. The client is also to be designed/architectured in a way that makes it easy for developers to add/remove features and improve upon the existing work. The code and file structure should be clean, organized and easy to read/navigate.
Behavior preservation: Vanilla Terraria behavior for valid input is the baseline contract. Performance optimizations, safety fixes, refactors, and internal modernization must preserve observable gameplay and multiplayer behavior unless a deliberately user-configurable feature explicitly changes presentation or QoL behavior. Optimize implementation, not game semantics.

# Overall Rules
Whenever implementing something, ensure the best optimization and programming standards and practices, code should be readable and efficient.
A file containing one primary type should normally match that type's name. Partial-type files should use TypeName.Responsibility.cs. Multiple closely related tiny types may share a file when doing so improves cohesion.


## Hot-path performance rules

Terraria update, draw, input, entity-snapshot, dispatcher, and scheduler paths are performance-sensitive.

Avoid recurring allocations in per-frame/per-update paths when an allocation-free implementation remains clear. In particular, avoid temporary LINQ pipelines, arrays, lists, strings, delegates, or helper objects on the common path merely for convenience.

Prefer lazy/reused temporary storage and caller-owned buffers where appropriate.

Do not replace simple bounded algorithms with complex structures such as heaps, timing wheels, pools, or custom collections without evidence that the simpler implementation is a meaningful bottleneck.

Optimize the common path first. A path where there is no work to perform should normally be extremely cheap.

---

## Plugin Integration Rule

Ordinary plugins must be independently installable and must not require plugin-specific changes to:

- `Alacrity.TerrariaIntegration`
- Terraria patch definitions
- shared bridge assemblies
- Alacrity Core runtime dispatch code

When adding a plugin, first implement it using existing host services such as:

- `context.Settings`
- `context.Keybinds`
- `context.Commands`
- `context.Ui`
- `context.Notifications`
- `context.Events`
- `context.Entities`
- `context.Overlays`
- `context.Storage`
- `context.Services`

These are just examples, ensure explicit names are used.

Do not create plugin-named runtime methods or classes such as (these are just examples, do not take it literally):

DrawHitboxesPlugin();
RunDamageMeterPlugin();
BossTimerRuntime;

when the feature can be expressed through a generic capability.

If the required capability does not exist:

Determine whether it is reusable by other plugins.
Add a framework-neutral SDK contract.
Add a host-owned Core implementation and registration lifetime.
Add one generic Terraria adapter or hook in Alacrity.TerrariaIntegration.
Implement the plugin only through that new capability.
Add cleanup, fault isolation, and tests.

Modify Alacrity.TerrariaIntegration only for reusable Terraria capabilities, version-specific adapters, or unavoidable engine-level patches.

A normal plugin addition should usually change only the plugin project and its tests.

Check Functionalities.md in docs for existing functionalities.

Prioritize clarity over cleverness, including in performance-sensitive code. Prefer the simplest implementation that satisfies measured performance requirements; avoid both unnecessary allocations and unnecessary abstraction/algorithmic complexity

## Repository-wide API organization

Keep `Alacrity.PluginSdk` and `Alacrity.Core` domain-oriented. Public contracts should live in focused areas such as `Plugins`, `Lifecycle`, `Settings`, `Commands`, `Events`, `Entities`, `Rendering`, `Scheduling`, `Multiplayer`, `Services`, and `Compatibility`; avoid adding unrelated contracts to large catch-all files.

`IPluginContext` is the public plugin boundary. Do not add a public global runtime, raw Terraria/XNA access, mutable game objects, manual cleanup requirements, or unscoped callbacks.

New public APIs must be framework-neutral, activation-scoped where applicable, implementable by a fake test host, documented for thread affinity and lifetime, and backed by a real Core/integration implementation and tests.

Prefer small focused files, domain-based namespaces, explicit package-versus-activation lifecycle terminology, immutable snapshots, generation-aware identity, typed registrations, and clear compatibility diagnostics.

Ensure standardization across the repository, Prioritize clarity over cleverness, including in performance-sensitive code. Prefer the simplest implementation that satisfies measured performance requirements; avoid both unnecessary allocations and unnecessary abstraction/algorithmic complexity

Use explicit names such as:

- `ClientStartedEvent`
- `ClientShuttingDownEvent`
- `WorldLoadedEvent`
- `WorldUnloadingEvent`
- `ServerConnectedEvent`
- `ServerDisconnectedEvent`
- `LocalPlayerSpawnedEvent`
- `GraphicsDeviceChangedEvent`
- `UiScaleChangedEvent`
- `PluginEnabledEvent`
- `PluginDisabledEvent`

These are just examples, do not take them literally


## Lifecycle and background-work rules

Plugin work is activation-scoped.

When an activation ends:

* stop admission of new activation-owned work;
* request cancellation of existing work;
* prevent callbacks from the old activation entering a later activation;
* observe failures and retain useful diagnostics;
* bound non-cooperative cleanup.

Normal plugin disable must not stop unrelated plugins or globally stop host scheduling.

Never synchronously wait on plugin/background tasks from Terraria's update or render thread when their completion may require main-thread progress. A timeout that freezes the game thread is not an acceptable substitute for asynchronous lifecycle coordination.

Do not hold host locks while invoking plugin code.

Lifecycle, scheduler, dispatcher, and resource ownership changes must explicitly consider disposal/cancellation/completion races and remain idempotent.

---

## Registration ownership rules

Transient registrations must not remain retained by an activation's `IPluginResourceScope` after the registration has permanently completed.

Concurrency-sensitive ownership logic such as:

* released state;
* late resource-handle attachment;
* exactly-once ownership release;
* disposal racing registration;

should use a shared, well-tested internal primitive where the semantics are the same rather than duplicated implementations.

Do not introduce synchronization state or counters unless they enforce a documented invariant.

---

## TerrariaIntegration organization

`Alacrity.TerrariaIntegration` must remain domain-oriented rather than a flat collection of Terraria adapters.

Place code under coherent domains such as `Bridge`, `Runtime`, `GameState`, `Rendering`, `Chat`, `Input`, `VisualEffects`, `Persistence`, `UserInterface`, and `Compatibility`, with matching namespaces.

The patch-facing bridge is a small, stable, versioned ABI. Existing patched entry-point signatures must not be changed casually; bridge methods should delegate to focused runtime subsystems rather than contain feature implementations.

Ordinary plugins must use framework-neutral, activation-scoped services through `IPluginContext`. Do not expose raw Terraria entities, XNA types, mutable game state, `SpriteBatch`, or public global runtime access.

New Terraria-specific code must implement a reusable capability. Do not add integration classes or bridge methods named after a bundled plugin.

Use explicit names such as:

- `ClientStartedEvent`
- `ClientShuttingDownEvent`
- `WorldLoadedEvent`
- `WorldUnloadingEvent`
- `ServerConnectedEvent`
- `ServerDisconnectedEvent`
- `LocalPlayerSpawnedEvent`
- `GraphicsDeviceChangedEvent`
- `UiScaleChangedEvent`
- `PluginEnabledEvent`
- `PluginDisabledEvent`

These are just examples, do not take them literally

The patch-facing `PluginUiRuntime` bridge is a thin, stable, versioned ABI facade.

Keep patch-facing static methods small and forward into focused internally owned runtime instances/subsystems.

Do not use partial files merely to hide a growing global God object. Runtime, menu, chat, rendering, input, and lifecycle state should belong to cohesive owners with clear lifetimes.

Do not casually change patch-referenced signatures.

---

## Terraria patching architecture

The patched client must be generated through the repository’s single authoritative patcher pipeline. Permanent Terraria patches must use explicit IDs, exact target signatures, deterministic ordering, verified IL anchors, post-patch validation, and stable bridge ABI methods.

Keep patch definitions, bridge forwarding methods, and runtime implementations separate. Never expose all Terraria internals, add ordinary-plugin raw IL hooks, discover core patches in arbitrary reflection order, or change a patch-referenced bridge signature without an explicit ABI migration and updated reflection tests.

Client staging must use assemblies from one coherent build, remove stale generated files, and emit compatibility metadata capable of detecting mismatched executables and DLLs before normal startup.

## Generated artifacts and staging

Source code is the authoritative repository state.

Generated runtime assemblies should normally be produced under a dedicated generated output such as `artifacts/` and should not be committed as ordinary source files.

A normal build must not silently deploy into the repository root or a live Terraria installation.

Deployment/staging to a client directory must be explicit.

All staged runtime assemblies must come from one coherent build and compatibility metadata must detect stale or mismatched SDK/Core/bridge/runtime components before normal startup.

Do not manually reconstruct referenced-project output paths when MSBuild can provide the actual target outputs.

---

## Testing rules

Use normal discoverable .NET test projects rather than large `Program.cs` test runners.

Organize tests by subsystem and keep test helpers/fakes focused and reusable.

Tests must not depend on execution order or leaked static mutable state.

Concurrency-sensitive code requires regression coverage for cancellation, disposal, completion, scope release, reactivation, timeouts, and exception observation where applicable.

Prefer deterministic seams such as fake monotonic clocks over sleeps for timing-sensitive unit tests.

---

## Formatting and reviewability

Code is written for review as well as execution.

Use one statement per line in normal code. Do not compress multiple fields, constructor assignments, or significant control-flow operations onto one line.

Concurrency and lifecycle code should favor explicit readable control flow over terseness.

Comments should explain invariants, lifetime, ownership, compatibility, performance rationale, or thread affinity rather than restating obvious syntax.

Keep repository formatting standardized through `.editorconfig` and remove unused imports in touched code.
