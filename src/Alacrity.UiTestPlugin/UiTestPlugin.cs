using Alacrity.PluginSdk;

namespace Alacrity.UiTestPlugin;

/// <summary>Minimal package used to exercise the public plugin lifecycle without game integration.</summary>
public sealed class UiTestPlugin : IAlacrityPlugin
{
    public PluginManifest Manifest { get; } = new PluginManifest(
        new PluginId("alacrity.ui-test"),
        "Alacrity UI Test Plugin",
        new System.Version(0, 1, 0),
        "ExoField",
        "A minimal lifecycle package used to verify Alacrity plugin discovery and ownership cleanup.",
        new[] { "1.4.5.6" },
        capabilities: PluginCapability.UserInterface,
        permissions: PluginPermission.DrawUserInterface,
        multiplayerSafety: MultiplayerSafety.ClientOnly,
        changelog: "0.1.0 - Minimal lifecycle test package.",
        entryAssembly: "Alacrity.UiTestPlugin.dll",
        entryType: "Alacrity.UiTestPlugin.UiTestPlugin");

    public void Initialize(IPluginContext context)
    {
        context.Logger.Info("Alacrity UI test plugin initialized.");
        if (context is IPluginContextV2 extended)
            extended.Ui.RegisterSettingsPage(new PluginUiContribution("ui-test-settings", "UI Test Settings"));
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown() { }
}
