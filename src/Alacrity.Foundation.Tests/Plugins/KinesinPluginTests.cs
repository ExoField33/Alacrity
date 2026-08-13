using System;
using System.Linq;
using Alacrity.Kinesin;
using Alacrity.PluginSdk;
using Xunit;

public sealed class KinesinPluginTests
{
    [Fact]
    public void Settings_ComposeAndWithdrawTheGenericPolicies()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new KinesinPlugin();

        plugin.Initialize(context);

        Assert.Equal(
            PluginRenderingOptimization.PaintedTilePreparation |
            PluginRenderingOptimization.ClothingEntityPresentation |
            PluginRenderingOptimization.WaterfallPresentation |
            PluginRenderingOptimization.TileDrawingPresentation |
            PluginRenderingOptimization.DrawOrchestration |
            PluginRenderingOptimization.LaserRulerPresentation |
            PluginRenderingOptimization.StaticTileChunkPresentation,
            host.RenderingOptimizations.GetEffectiveOptimizations());
        PluginSettingControl[] settings = host.GetSettingsControls(manifest.Id).ToArray();
        Assert.Equal(7, settings.Length);
        PluginSettingControl paintedTiles = Assert.Single(settings, setting => setting.Id == "painted-tile-preparation");
        PluginSettingControl clothingEntities = Assert.Single(settings, setting => setting.Id == "clothing-entity-presentation");
        PluginSettingControl waterfalls = Assert.Single(settings, setting => setting.Id == "waterfall-presentation");
        PluginSettingControl tileDrawing = Assert.Single(settings, setting => setting.Id == "tile-drawing-presentation");
        PluginSettingControl drawOrchestration = Assert.Single(settings, setting => setting.Id == "draw-orchestration");
        PluginSettingControl laserRuler = Assert.Single(settings, setting => setting.Id == "laser-ruler-presentation");
        PluginSettingControl staticTileChunks = Assert.Single(settings, setting => setting.Id == "static-tile-chunk-presentation");

        paintedTiles.SetToggle!(false);
        Assert.Equal(
            PluginRenderingOptimization.ClothingEntityPresentation |
            PluginRenderingOptimization.WaterfallPresentation |
            PluginRenderingOptimization.TileDrawingPresentation |
            PluginRenderingOptimization.DrawOrchestration |
            PluginRenderingOptimization.LaserRulerPresentation |
            PluginRenderingOptimization.StaticTileChunkPresentation,
            host.RenderingOptimizations.GetEffectiveOptimizations());

        clothingEntities.SetToggle!(false);
        Assert.Equal(
            PluginRenderingOptimization.WaterfallPresentation |
            PluginRenderingOptimization.TileDrawingPresentation |
            PluginRenderingOptimization.DrawOrchestration |
            PluginRenderingOptimization.LaserRulerPresentation |
            PluginRenderingOptimization.StaticTileChunkPresentation,
            host.RenderingOptimizations.GetEffectiveOptimizations());

        waterfalls.SetToggle!(false);
        tileDrawing.SetToggle!(false);
        drawOrchestration.SetToggle!(false);
        laserRuler.SetToggle!(false);
        staticTileChunks.SetToggle!(false);
        Assert.Equal(PluginRenderingOptimization.None, host.RenderingOptimizations.GetEffectiveOptimizations());

        paintedTiles.SetToggle!(true);
        Assert.Equal(PluginRenderingOptimization.PaintedTilePreparation, host.RenderingOptimizations.GetEffectiveOptimizations());

        plugin.Disable();
        Assert.Equal(PluginRenderingOptimization.None, host.RenderingOptimizations.GetEffectiveOptimizations());
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("alacrity.kinesin"),
            "Kinesin",
            new Version(0, 1),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Rendering,
            permissions: PluginPermission.DrawUserInterface,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
    }
}
