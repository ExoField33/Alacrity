# Plugin Scheduling

Use `context.Scheduler` for work that must outlive the current callback. Every returned
`IPluginRegistration` is activation-owned and is cancelled automatically when the plugin disables,
faults, or unloads. Repeating work never queues a second pending callback while an earlier delivery
is waiting for the main-thread dispatcher.

`NextUpdate`, `AfterUpdates`, `EveryUpdates`, `After`, and `Every` invoke their actions through the
host main-thread dispatcher. Use `RunBackground` only for independent file or network work and
marshal immutable results back through `context.Dispatcher`; background work must never read live
Terraria state directly.

Elapsed delays use the host monotonic clock rather than wall-clock time. The scheduler converts
`TimeSpan` values into that clock's frequency-safe unit, so a client with a non-10MHz stopwatch
does not run elapsed work early or late because of tick-unit assumptions. Background work is
bounded per plugin activation. It is cancelled during disable and shutdown, and the host observes
completion for a bounded period without blocking update or render callbacks indefinitely.
