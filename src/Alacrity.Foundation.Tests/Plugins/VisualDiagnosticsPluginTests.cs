using System;
using Alacrity.PluginSdk;
using Alacrity.VisualDiagnostics;
using Xunit;

public sealed class VisualDiagnosticsPluginTests
{
    [Fact]
    public void Initialize_RegistersOnlyGenericWorldPresentation()
    {
        using var host = new FakePluginHost();
        var manifest = new PluginManifest(
            new PluginId("alacrity.visual-diagnostics"),
            "Visual Diagnostics",
            new Version(0, 1),
            "Tests",
            "Diagnostics",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Rendering | PluginCapability.GameStateRead | PluginCapability.Diagnostics,
            permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
        var context = host.Create(manifest);
        var plugin = new VisualDiagnosticsPlugin();

        plugin.Initialize(context);

        Assert.Single(host.GetSettingsPages(manifest.Id));
        Assert.Equal(5, host.GetSettingsControls(manifest.Id).Count);
        Assert.False(host.Overlays.HasRegistrations(PluginOverlaySpace.World));

        PluginSettingControl aggro = Assert.Single(host.GetSettingsControls(manifest.Id), control => control.Id == "hostile-npc-aggro-lines");
        aggro.SetToggle!(true);
        Assert.True(host.Overlays.HasRegistrations(PluginOverlaySpace.World));
        aggro.SetToggle!(false);
        Assert.False(host.Overlays.HasRegistrations(PluginOverlaySpace.World));

        context.Resources.Dispose();
        Assert.Empty(host.GetSettingsPages(manifest.Id));
        Assert.Empty(host.GetSettingsControls(manifest.Id));
    }
}
