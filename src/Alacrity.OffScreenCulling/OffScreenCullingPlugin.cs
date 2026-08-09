using System;
using Alacrity.PluginSdk;

namespace Alacrity.OffScreenCulling;

/// <summary>Publishes conservative local world-render culling requests through the generic host service.</summary>
public sealed class OffScreenCullingPlugin : IAlacrityPlugin
{
    private IPluginRenderCullingService? renderCulling;
    private IPluginRegistration? policyRegistration;
    private IPluginSetting<bool>? playerCullingSetting;
    private IPluginSetting<bool>? itemCullingSetting;
    private IPluginSetting<bool>? dustCullingSetting;
    private IPluginSetting<bool>? particleCullingSetting;
    private bool playerCulling = true;
    private bool itemCulling = true;
    private bool dustCulling = true;
    private bool particleCulling = true;

    public void Initialize(IPluginContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        renderCulling = context.Terraria.RenderCulling;
        playerCullingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("playerRenderCulling", true));
        itemCullingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("itemVisualCulling", true));
        dustCullingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("dustCulling", true));
        particleCullingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("particleCulling", true));

        playerCulling = playerCullingSetting.Value;
        itemCulling = itemCullingSetting.Value;
        dustCulling = dustCullingSetting.Value;
        particleCulling = particleCullingSetting.Value;

        playerCullingSetting.Subscribe(value => { playerCulling = value; RefreshPolicy(); });
        itemCullingSetting.Subscribe(value => { itemCulling = value; RefreshPolicy(); });
        dustCullingSetting.Subscribe(value => { dustCulling = value; RefreshPolicy(); });
        particleCullingSetting.Subscribe(value => { particleCulling = value; RefreshPolicy(); });

        context.Ui.RegisterSettingsPage(new PluginUiContribution("off-screen-culling", "Off-screen Culling"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("player-render-culling", "Player Render Culling", playerCullingSetting).InPage("off-screen-culling"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("item-visual-culling", "Item Visual Culling", itemCullingSetting).InPage("off-screen-culling"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("dust-culling", "Dust Culling", dustCullingSetting).InPage("off-screen-culling"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("particle-culling", "World Particle Culling", particleCullingSetting).InPage("off-screen-culling"));
        RefreshPolicy();
    }

    public void Enable() { }

    public void Disable()
    {
        policyRegistration?.Dispose();
        policyRegistration = null;
        renderCulling = null;
    }

    public void Shutdown()
    {
        Disable();
        playerCullingSetting = null;
        itemCullingSetting = null;
        dustCullingSetting = null;
        particleCullingSetting = null;
    }

    private void RefreshPolicy()
    {
        IPluginRenderCullingService? service = renderCulling;
        if (service == null) return;

        policyRegistration?.Dispose();
        PluginRenderCullingCategory categories = PluginRenderCullingCategory.None;
        if (playerCulling) categories |= PluginRenderCullingCategory.Players;
        if (itemCulling) categories |= PluginRenderCullingCategory.DroppedItems;
        if (dustCulling) categories |= PluginRenderCullingCategory.Dust;
        if (particleCulling) categories |= PluginRenderCullingCategory.WorldParticles;
        policyRegistration = service.RegisterPolicy(new PluginRenderCullingPolicy(categories));
    }
}
