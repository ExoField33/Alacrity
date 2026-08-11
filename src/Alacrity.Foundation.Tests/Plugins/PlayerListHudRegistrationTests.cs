using System;
using Alacrity.Core;
using Alacrity.PlayerList;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PlayerListHudRegistrationTests
{
    [Fact]
    public void HiddenPlayerListDoesNotRetainAHudWidget()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = new PluginManifest(
            new PluginId("alacrity.player-list"),
            "Player List",
            new Version(0, 1),
            "Tests",
            "Player List tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Input | PluginCapability.GameStateRead | PluginCapability.MultiplayerObservation,
            permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState | PluginPermission.ObserveMultiplayer,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
        PluginHostContext context = host.Create(manifest);
        var plugin = new PlayerListPlugin();

        plugin.Initialize(context);
        Assert.False(host.Hud.HasRegistrations());

        plugin.SetVisibility(true);
        Assert.True(host.Hud.HasRegistrations());

        plugin.SetVisibility(false);
        Assert.False(host.Hud.HasRegistrations());

        plugin.Disable();
        Assert.False(host.Hud.HasRegistrations());
    }
}
