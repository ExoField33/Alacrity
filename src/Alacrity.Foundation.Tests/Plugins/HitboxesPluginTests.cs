using System;
using Alacrity.Hitboxes;
using Alacrity.PluginSdk;
using Xunit;

public sealed class HitboxesPluginTests
{
    [Fact]
    public void WorldOverlayExistsOnlyWhileAtLeastOneHitboxCategoryIsEnabled()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = new PluginManifest(
            new PluginId("alacrity.hitboxes"),
            "Hitboxes",
            new Version(0, 1),
            "Tests",
            "Hitbox tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Rendering | PluginCapability.GameStateRead,
            permissions: PluginPermission.DrawUserInterface | PluginPermission.ReadGameState,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
        var context = host.Create(manifest);
        var plugin = new HitboxesPlugin();

        plugin.Initialize(context);
        Assert.False(host.Overlays.HasRegistrations(PluginOverlaySpace.World));

        PluginSettingControl players = Assert.Single(host.GetSettingsControls(manifest.Id), control => control.Id == "player-hitboxes");
        players.SetToggle!(true);
        Assert.True(host.Overlays.HasRegistrations(PluginOverlaySpace.World));
        players.SetToggle!(false);
        Assert.False(host.Overlays.HasRegistrations(PluginOverlaySpace.World));

        plugin.Disable();
        Assert.False(host.Overlays.HasRegistrations(PluginOverlaySpace.World));
    }
}
