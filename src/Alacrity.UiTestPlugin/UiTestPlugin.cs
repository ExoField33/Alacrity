using Alacrity.PluginSdk;

namespace Alacrity.UiTestPlugin;

/// <summary>Minimal package used to exercise the public plugin lifecycle without game integration.</summary>
public sealed class UiTestPlugin : IAlacrityPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.Logger.Info("Alacrity UI test plugin initialized.");
        var showDiagnostics = context.Settings.Get("showDiagnostics", true);
        var accent = context.Settings.Get("accent", "Green");
        var opacity = context.Settings.Get("opacity", 0.75f);
        var accentColor = context.Settings.Get("accentColor", "#37C871");
        context.Settings.Set("showDiagnostics", showDiagnostics);
        context.Settings.Set("accent", accent);
        context.Settings.Set("opacity", opacity);
        context.Settings.Set("accentColor", accentColor);
        context.Ui.RegisterSettingsPage(new PluginUiContribution("ui-test-settings", "UI Test Settings"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle(
                "ui-test-diagnostics",
                "Show diagnostics",
                () => context.Settings.Get("showDiagnostics", true),
                value => context.Settings.Set("showDiagnostics", value)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle(
                "ui-test-accent",
                "Accent",
                new[] { "Green", "Blue", "Purple" },
                () => context.Settings.Get("accent", "Green"),
                value => context.Settings.Set("accent", value)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider(
                "ui-test-opacity", "Overlay opacity", 0f, 1f, 0.05f,
                () => context.Settings.Get("opacity", 0.75f),
                value => context.Settings.Set("opacity", value),
                value => (int)(value * 100f) + "%"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color(
                "ui-test-accent-color", "Accent color",
                () => PluginColor.TryParseHex(context.Settings.Get("accentColor", "#37C871"), out var color) ? color : new PluginColor(55, 200, 113),
                value => context.Settings.Set("accentColor", value.ToHex())));
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown() { }
}
