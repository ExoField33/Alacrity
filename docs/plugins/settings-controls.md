# Plugin Settings Controls

Plugins register host-rendered settings through `IPluginContext.Ui`. The host owns layout,
input, persistence callbacks, cleanup, and Terraria-specific UI assets.

```csharp
context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle(
    "show-overlay", "Show overlay",
    () => context.Settings.Get("showOverlay", true),
    value => context.Settings.Set("showOverlay", value)));

context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle(
    "detail", "Detail", new[] { "Full", "Minimal", "Disabled" },
    () => context.Settings.Get("detail", "Full"),
    value => context.Settings.Set("detail", value)));

context.Ui.RegisterSettingsControl(PluginSettingControl.Slider(
    "opacity", "Opacity", 0f, 1f, 0.05f,
    () => context.Settings.Get("opacity", 0.75f),
    value => context.Settings.Set("opacity", value),
    value => (int)(value * 100f) + "%"));

context.Ui.RegisterSettingsControl(PluginSettingControl.Color(
    "accent", "Accent color",
    () => PluginColor.TryParseHex(context.Settings.Get("accent", "#37C871"), out var color)
        ? color : new PluginColor(55, 200, 113),
    color => context.Settings.Set("accent", color.ToHex())));
```

`Color` produces a compact current-color swatch plus canonical `#RRGGBB` copy and paste actions.
All registrations are attached to the plugin resource scope and are removed automatically when the
plugin disables. `PluginUiContribution` remains supported for legacy one-click settings, but new
plugins should use `PluginSettingControl` for typed behavior.
