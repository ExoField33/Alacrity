using System;
using Alacrity.PluginSdk;

namespace Alacrity.Kinesin;

/// <summary>
/// Hosts conservative, behavior-preserving engine preparation optimizations through generic
/// platform policies. Kinesin never receives raw Terraria or XNA rendering objects.
/// </summary>
public sealed class KinesinPlugin : IAlacrityPlugin
{
    private IPluginRenderingOptimizationService? renderingOptimizations;
    private IPluginRegistration? policyRegistration;
    private bool paintedTilePreparationEnabled = true;
    private bool clothingEntityPresentationEnabled = true;
    private bool waterfallPresentationEnabled = true;
    private bool tileDrawingPresentationEnabled = true;
    private bool drawOrchestrationEnabled = true;
    private bool laserRulerPresentationEnabled = true;
    private bool staticTileChunkPresentationEnabled = true;
    private bool rainPresentationEnabled = true;
    private bool lightingParallelismEnabled = true;

    public void Initialize(IPluginContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        renderingOptimizations = context.Terraria.RenderingOptimizations;
        IPluginSetting<bool> paintedTilePreparationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("paintedTilePreparation", true));
        paintedTilePreparationEnabled = paintedTilePreparationSetting.Value;
        paintedTilePreparationSetting.Subscribe(value =>
        {
            paintedTilePreparationEnabled = value;
            RefreshPolicy();
        });
        IPluginSetting<bool> clothingEntityPresentationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("clothingEntityPresentation", true));
        clothingEntityPresentationEnabled = clothingEntityPresentationSetting.Value;
        clothingEntityPresentationSetting.Subscribe(value =>
        {
            clothingEntityPresentationEnabled = value;
            RefreshPolicy();
        });

        context.Ui.RegisterSettingsPage(new PluginUiContribution("kinesin", "Kinesin"));
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "painted-tile-preparation",
                "Optimize Painted Tile Preparation",
                paintedTilePreparationSetting).InPage("kinesin"));
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "clothing-entity-presentation",
                "Optimize Clothing Entity Rendering",
                clothingEntityPresentationSetting).InPage("kinesin"));
        IPluginSetting<bool> waterfallPresentationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("waterfallPresentation", true));
        waterfallPresentationEnabled = waterfallPresentationSetting.Value;
        waterfallPresentationSetting.Subscribe(value =>
        {
            waterfallPresentationEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "waterfall-presentation",
                "Optimize Waterfall Rendering",
                waterfallPresentationSetting).InPage("kinesin"));
        IPluginSetting<bool> tileDrawingPresentationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("tileDrawingPresentation", true));
        tileDrawingPresentationEnabled = tileDrawingPresentationSetting.Value;
        tileDrawingPresentationSetting.Subscribe(value =>
        {
            tileDrawingPresentationEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "tile-drawing-presentation",
                "Optimize Tile Drawing",
                tileDrawingPresentationSetting).InPage("kinesin"));
        IPluginSetting<bool> drawOrchestrationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("drawOrchestration", true));
        drawOrchestrationEnabled = drawOrchestrationSetting.Value;
        drawOrchestrationSetting.Subscribe(value =>
        {
            drawOrchestrationEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "draw-orchestration",
                "Optimize Draw Orchestration",
                drawOrchestrationSetting).InPage("kinesin"));
        IPluginSetting<bool> laserRulerPresentationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("laserRulerPresentation", true));
        laserRulerPresentationEnabled = laserRulerPresentationSetting.Value;
        laserRulerPresentationSetting.Subscribe(value =>
        {
            laserRulerPresentationEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "laser-ruler-presentation",
                "Optimize Laser Ruler Rendering",
                laserRulerPresentationSetting).InPage("kinesin"));
        IPluginSetting<bool> staticTileChunkPresentationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("staticTileChunkPresentation", true));
        staticTileChunkPresentationEnabled = staticTileChunkPresentationSetting.Value;
        staticTileChunkPresentationSetting.Subscribe(value =>
        {
            staticTileChunkPresentationEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "static-tile-chunk-presentation",
                "Optimize Static Tile Chunks",
                staticTileChunkPresentationSetting).InPage("kinesin"));
        IPluginSetting<bool> rainPresentationSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("rainPresentation", true));
        rainPresentationEnabled = rainPresentationSetting.Value;
        rainPresentationSetting.Subscribe(value =>
        {
            rainPresentationEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "rain-presentation",
                "Optimize Rain Rendering",
                rainPresentationSetting).InPage("kinesin"));
        IPluginSetting<bool> lightingParallelismSetting = context.Settings.Register(
            new PluginSettingDefinition<bool>("lightingParallelism", true));
        lightingParallelismEnabled = lightingParallelismSetting.Value;
        lightingParallelismSetting.Subscribe(value =>
        {
            lightingParallelismEnabled = value;
            RefreshPolicy();
        });
        context.Ui.RegisterSettingsControl(
            PluginSettingControl.Toggle(
                "lighting-parallelism",
                "Optimize Lighting Parallelism",
                lightingParallelismSetting).InPage("kinesin"));
        RefreshPolicy();
    }

    public void Enable()
    {
        RefreshPolicy();
    }

    public void Disable()
    {
        policyRegistration?.Dispose();
        policyRegistration = null;
        renderingOptimizations = null;
    }

    public void Shutdown()
    {
        Disable();
    }

    private void RefreshPolicy()
    {
        IPluginRenderingOptimizationService? service = renderingOptimizations;
        if (service == null)
        {
            return;
        }

        policyRegistration?.Dispose();
        policyRegistration = null;
        PluginRenderingOptimization optimizations = PluginRenderingOptimization.None;
        if (paintedTilePreparationEnabled)
        {
            optimizations |= PluginRenderingOptimization.PaintedTilePreparation;
        }
        if (clothingEntityPresentationEnabled)
        {
            optimizations |= PluginRenderingOptimization.ClothingEntityPresentation;
        }
        if (waterfallPresentationEnabled)
        {
            optimizations |= PluginRenderingOptimization.WaterfallPresentation;
        }
        if (tileDrawingPresentationEnabled)
        {
            optimizations |= PluginRenderingOptimization.TileDrawingPresentation;
        }
        if (drawOrchestrationEnabled)
        {
            optimizations |= PluginRenderingOptimization.DrawOrchestration;
        }
        if (laserRulerPresentationEnabled)
        {
            optimizations |= PluginRenderingOptimization.LaserRulerPresentation;
        }
        if (staticTileChunkPresentationEnabled)
        {
            optimizations |= PluginRenderingOptimization.StaticTileChunkPresentation;
        }
        if (rainPresentationEnabled)
        {
            optimizations |= PluginRenderingOptimization.RainPresentation;
        }
        if (lightingParallelismEnabled)
        {
            optimizations |= PluginRenderingOptimization.LightingParallelism;
        }
        if (optimizations == PluginRenderingOptimization.None)
        {
            return;
        }

        policyRegistration = service.RegisterPolicy(
            new PluginRenderingOptimizationPolicy(optimizations));
    }
}
