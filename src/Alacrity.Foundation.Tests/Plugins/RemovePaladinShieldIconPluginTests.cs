using System;
using Alacrity.RemovePaladinShieldIcon;
using Alacrity.PluginSdk;
using Xunit;

public sealed class RemovePaladinShieldIconPluginTests
{
    [Fact]
    public void EnableAndDisable_ComposeAndWithdrawTheGenericPresentationRequest()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var plugin = new RemovePaladinShieldIconPlugin();

        plugin.Initialize(host.Create(manifest));
        Assert.Equal(PluginPresentationElement.None, host.PresentationSuppressions.GetEffectiveElements());

        plugin.Enable();
        Assert.Equal(PluginPresentationElement.PaladinShieldIcon, host.PresentationSuppressions.GetEffectiveElements());

        plugin.Disable();
        Assert.Equal(PluginPresentationElement.None, host.PresentationSuppressions.GetEffectiveElements());

        plugin.Shutdown();
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("alacrity.remove-paladin-shield-icon"),
            "Remove Paladin Shield Icon",
            new Version(0, 1),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Rendering,
            permissions: PluginPermission.None,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
    }
}
