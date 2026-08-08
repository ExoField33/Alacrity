# Runtime Lifecycle and Background Work

Every enabled plugin has an activation-local context and resource scope. Disabling an
activation closes admission immediately, cancels its scheduler and background work, and
releases registrations in reverse order. A later enable creates a new context; callbacks
from the old activation cannot register work into it.

`IPluginScheduler.RunBackground` is scoped and bounded per plugin. The scheduler retains
and observes the task, treats cooperative cancellation as normal, and attributes other
failures to the owning plugin logger. Background cancellation is requested at disable and
shutdown. Synchronous lifecycle paths never wait for an incomplete background task on the
Terraria update or render thread; bounded completion is observed asynchronously. Async
lifecycle paths can await the same bounded drain as part of their operation coordinator.

Plugin-manager actions use the same rule. An asynchronous enable or disable begins at the
Terraria UI boundary and is polled from a later update; completion, cancellation, and failure
are reported without waiting on a task in the menu click handler. Persisted startup activation
uses that coordinator too, so restoring an asynchronous plugin cannot freeze client startup.
`RequestReloadAsync` is the corresponding non-blocking Core API for an asynchronous plugin;
the synchronous overload rejects that contract rather than hiding a multi-second wait.

Dispatcher and scheduled registrations are owned before publication. Transient work drops
its scope ownership when it completes or is cancelled, so long-running activations do not
retain completed callback objects. The scheduler uses a monotonic clock for elapsed work
and update counters for tick work; both paths are bounded and avoid executing plugin code
while host locks are held.

## Test execution

The Foundation and Terraria integration scenario suites are normal xUnit theory cases. Foundation
coverage is grouped under lifecycle, scheduling, packages, settings/plugins, chat, presentation,
patching, core, and tile-storage test classes; integration coverage is grouped under bridge,
game-state, and rendering test classes. Each retained scenario is reported independently by
`dotnet test`, which makes failures filterable by scenario name. The optional real `GraphicsDevice`
probe is not part of normal headless test execution:
legacy XNA device disposal is environment-sensitive, so it is exercised only on a local client
machine when validating graphics-device replacement.
