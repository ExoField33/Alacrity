# Plugin Commands

`context.Commands` is the only plugin command registration surface. Every registration is owned by
the current activation scope and is removed automatically when that activation disables, faults, or
shuts down. Commands are dispatched locally by the authoritative Core `PluginCommandHost`; a
throwing handler is consumed locally, logged with its plugin and command IDs, and cannot fall
through to multiplayer chat.

## Explicit Commands

Use the explicit API when command syntax is unusual or deliberately custom:

```csharp
context.Commands.Register(
    new PluginCommandDescriptor("de", "Manages Dust ID exceptions"),
    invocation =>
    {
        // Parse invocation.Arguments explicitly.
    });
```

This API remains authoritative and fully supported.

## Typed Commands

For conventional positional commands, use the optional fluent builder. The builder performs all
declaration validation during initialization, then registers one normal command with the same host.
It is ergonomic sugar, not a second command parser or dispatcher.

```csharp
var command = context.Commands.Define("marker", "Adds a named marker")
    .Alias("mark");

PluginTypedCommandParameter<string> name = command.RequiredString("name", "Marker label");
PluginTypedCommandParameter<int> duration = command.OptionalInt32("seconds", 30, 1, 300);
PluginTypedCommandParameter<bool> visible = command.OptionalBoolean("visible", true);

command.Register(arguments =>
{
    AddMarker(arguments.Get(name), arguments.Get(duration), arguments.Get(visible));
});
```

Supported positional values are strings, `Int32`, finite `Single`, booleans, enums, and declared
case-insensitive choices. Numeric ranges and custom validators are declared when parameters are
added. Required parameters must precede optional parameters. Aliases, parameter names, required
state, descriptions, ranges, defaults, and known choices are retained on `PluginCommandDescriptor`
for help or future completion UI.

The Terraria chat adapter tokenizes quoted single- and double-quoted arguments before dispatch.
Malformed quoted input receives safe local feedback. Typed conversion and validation failures also
reply locally and do not invoke the plugin handler.
The adapter tokenizes only a command name currently owned by the plugin host; unknown slash input
continues to Terraria unchanged for vanilla or server command handling.

Command handlers run on the host command-dispatch path, currently initiated by Terraria chat input.
They should stay short and schedule longer work through `context.Scheduler` or
`context.Dispatcher` as appropriate. Plugins do not own manual cleanup: the activation scope does.
