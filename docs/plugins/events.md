# Events

Subscribe with `context.Events.Subscribe<TEvent>(...)`; subscriptions are released automatically with the activation. Events are immutable snapshots and handlers are invoked independently so one failure cannot prevent later handlers.

## Verified delivery

The active Terraria bridge currently publishes only these verified client events:

- `ClientStartedEvent`: once, after runtime bootstrap and persisted activation restoration.
- `ClientUpdatedEvent`: from the established Terraria gameplay-update hook, after shared snapshots are captured and before gameplay keybind dispatch is considered.
- `ClientShuttingDownEvent`: once, before deterministic activation shutdown begins.
- `ClientMenuStateChangedEvent`: when the verified `Terraria.Main.gameMenu` state changes. It describes menu versus gameplay presentation and does not claim a completed world or server lifecycle.
- `ChatInputStateChangedEvent`: when the verified `Terraria.Main.drawingPlayerChat` state changes.
- `WorldOverlayRenderingEvent`, `HudRenderingEvent`, and `MenuRenderingEvent`: from their matching verified draw entry points. They are render-phase metadata events and do not provide a drawing context.

All events above are main-thread and non-cancellable. `ClientUpdatedEvent` handlers must remain small because they run in Terraria's update phase; use `context.Scheduler` for deferred or interval work.

World, multiplayer, graphics, and player lifecycle event names are deliberately not public yet. The host will add them only after each has a verified version-locked Terraria hook and documented phase.
