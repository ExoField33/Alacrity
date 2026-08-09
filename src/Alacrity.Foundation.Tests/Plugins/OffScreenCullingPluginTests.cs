using System;
using Alacrity.OffScreenCulling;
using Alacrity.PluginSdk;
using Xunit;

public sealed class OffScreenCullingPluginTests
{
    [Fact]
    public void Initialize_RegistersScopedCullingSettingsAndCleansThemUp()
    {
        using var host = new FakePluginHost();
        var manifest = new PluginManifest(
            new PluginId("alacrity.off-screen-culling"),
            "Off-screen Culling",
            new Version(0, 1),
            "Tests",
            "Presentation",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Rendering,
            permissions: PluginPermission.DrawUserInterface,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
        var context = host.Create(manifest);
        var plugin = new OffScreenCullingPlugin();

        plugin.Initialize(context);

        Assert.Single(host.GetSettingsPages(manifest.Id));
        Assert.Equal(4, host.GetSettingsControls(manifest.Id).Count);

        context.Resources.Dispose();

        Assert.Empty(host.GetSettingsPages(manifest.Id));
        Assert.Empty(host.GetSettingsControls(manifest.Id));
    }
}
