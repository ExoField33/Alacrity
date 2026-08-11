using System;
using Alacrity.PluginSdk;

namespace Alacrity.Hitboxes;

/// <summary>Renders host-provided collision snapshots through the framework-neutral overlay service.</summary>
public sealed class HitboxesPlugin : IAlacrityPlugin
{
    private IPluginEntitySnapshotService? entities;
    private IPluginMeleeCollisionSnapshotService? meleeSnapshots;
    private IPluginOverlayService? overlays;
    private IPluginRegistration? overlayRegistration;
    private IPluginRegistration? meleeCaptureDemand;
    private IPluginSetting<bool>? showPlayersSetting;
    private IPluginSetting<bool>? showNpcsSetting;
    private IPluginSetting<bool>? showProjectilesSetting;
    private IPluginSetting<bool>? showFriendlyProjectilesSetting;
    private IPluginSetting<bool>? showHostileProjectilesSetting;
    private IPluginSetting<bool>? showSwingsSetting;
    private IPluginSetting<string>? playerColorSetting;
    private IPluginSetting<string>? npcColorSetting;
    private IPluginSetting<string>? friendlyProjectileColorSetting;
    private IPluginSetting<string>? hostileProjectileColorSetting;
    private IPluginSetting<string>? swingColorSetting;
    private readonly System.Collections.Generic.List<PluginEntitySnapshot> entityBuffer = new System.Collections.Generic.List<PluginEntitySnapshot>(512);
    private readonly System.Collections.Generic.List<PluginEntitySnapshot> swingBuffer = new System.Collections.Generic.List<PluginEntitySnapshot>(32);
    private bool showPlayers;
    private bool showNpcs;
    private bool showProjectiles;
    private bool showFriendlyProjectiles = true;
    private bool showHostileProjectiles = true;
    private bool showSwings;
    private PluginColor playerColor = new PluginColor(90, 170, 255);
    private PluginColor npcColor = new PluginColor(255, 90, 90);
    private PluginColor friendlyProjectileColor = new PluginColor(90, 255, 110);
    private PluginColor hostileProjectileColor = new PluginColor(255, 230, 90);
    private PluginColor swingColor = new PluginColor(255, 150, 60);

    public void Initialize(IPluginContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        entities = context.Terraria.Entities;
        meleeSnapshots = entities as IPluginMeleeCollisionSnapshotService;
        showPlayersSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showPlayerHitboxes", false));
        showNpcsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showNpcHitboxes", false));
        showProjectilesSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showProjectileHitboxes", false));
        showFriendlyProjectilesSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showFriendlyProjectileHitboxes", true));
        showHostileProjectilesSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showHostileProjectileHitboxes", true));
        showSwingsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showSwingHitboxes", false));
        playerColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("playerHitboxColor", playerColor.ToHex()));
        npcColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("npcHitboxColor", npcColor.ToHex()));
        friendlyProjectileColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("friendlyProjectileHitboxColor", friendlyProjectileColor.ToHex()));
        hostileProjectileColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("hostileProjectileHitboxColor", hostileProjectileColor.ToHex()));
        swingColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("swingHitboxColor", swingColor.ToHex()));
        overlays = context.Overlays;
        showPlayers = showPlayersSetting.Value;
        showNpcs = showNpcsSetting.Value;
        showProjectiles = showProjectilesSetting.Value;
        showFriendlyProjectiles = showFriendlyProjectilesSetting.Value;
        showHostileProjectiles = showHostileProjectilesSetting.Value;
        showSwings = showSwingsSetting.Value;
        UpdateMeleeCaptureDemand();
        UpdateOverlayRegistration();
        playerColor = ReadColor(playerColorSetting.Value, playerColor); npcColor = ReadColor(npcColorSetting.Value, npcColor);
        friendlyProjectileColor = ReadColor(friendlyProjectileColorSetting.Value, friendlyProjectileColor); hostileProjectileColor = ReadColor(hostileProjectileColorSetting.Value, hostileProjectileColor); swingColor = ReadColor(swingColorSetting.Value, swingColor);
        showPlayersSetting.Subscribe(value => { showPlayers = value; UpdateOverlayRegistration(); });
        showNpcsSetting.Subscribe(value => { showNpcs = value; UpdateOverlayRegistration(); });
        showProjectilesSetting.Subscribe(value => { showProjectiles = value; UpdateOverlayRegistration(); });
        showFriendlyProjectilesSetting.Subscribe(value => showFriendlyProjectiles = value);
        showHostileProjectilesSetting.Subscribe(value => showHostileProjectiles = value);
        showSwingsSetting.Subscribe(value => { showSwings = value; UpdateMeleeCaptureDemand(); UpdateOverlayRegistration(); });
        playerColorSetting.Subscribe(value => playerColor = ReadColor(value, playerColor)); npcColorSetting.Subscribe(value => npcColor = ReadColor(value, npcColor));
        friendlyProjectileColorSetting.Subscribe(value => friendlyProjectileColor = ReadColor(value, friendlyProjectileColor)); hostileProjectileColorSetting.Subscribe(value => hostileProjectileColor = ReadColor(value, hostileProjectileColor)); swingColorSetting.Subscribe(value => swingColor = ReadColor(value, swingColor));

        context.Ui.RegisterSettingsPage(new PluginUiContribution("hitboxes", "Hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("player-hitboxes", "Player Hitboxes", showPlayersSetting).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("npc-hitboxes", "NPC Hitboxes", showNpcsSetting).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("projectile-hitboxes", "Projectile Hitboxes", showProjectilesSetting).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("friendly-projectile-hitboxes", "Friendly Projectile Hitboxes", showFriendlyProjectilesSetting).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("hostile-projectile-hitboxes", "Hostile Projectile Hitboxes", showHostileProjectilesSetting).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("swing-hitboxes", "Swing Hitboxes", showSwingsSetting).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("player-hitbox-color", "Player Hitbox Color", playerColorSetting, playerColor).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("npc-hitbox-color", "NPC Hitbox Color", npcColorSetting, npcColor).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("friendly-projectile-hitbox-color", "Friendly Projectile Color", friendlyProjectileColorSetting, friendlyProjectileColor).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("hostile-projectile-hitbox-color", "Hostile Projectile Color", hostileProjectileColorSetting, hostileProjectileColor).InPage("hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("swing-hitbox-color", "Swing Hitbox Color", swingColorSetting, swingColor).InPage("hitboxes"));
    }

    public void Enable() { }
    public void Disable()
    {
        meleeCaptureDemand?.Dispose();
        meleeCaptureDemand = null;
        overlayRegistration?.Dispose();
        overlayRegistration = null;
        overlays = null;
        meleeSnapshots = null;
        entities = null;
        entityBuffer.Clear();
        swingBuffer.Clear();
    }
    public void Shutdown()
    {
        Disable();
        showPlayersSetting = null;
        showNpcsSetting = null;
        showProjectilesSetting = null;
        showFriendlyProjectilesSetting = null;
        showHostileProjectilesSetting = null;
        showSwingsSetting = null;
        playerColorSetting = null;
        npcColorSetting = null;
        friendlyProjectileColorSetting = null;
        hostileProjectileColorSetting = null;
        swingColorSetting = null;
    }

    private static PluginColor ReadColor(string value, PluginColor fallback) => PluginColor.TryParseHex(value, out PluginColor parsed) ? parsed : fallback;

    private void UpdateMeleeCaptureDemand()
    {
        if (showSwings)
        {
            if ((meleeCaptureDemand == null || meleeCaptureDemand.IsReleased) && meleeSnapshots != null)
                meleeCaptureDemand = meleeSnapshots.RequestMeleeCollisionSnapshots();
        }
        else
        {
            meleeCaptureDemand?.Dispose();
            meleeCaptureDemand = null;
        }
    }

    /// <summary>Only active visual modes retain a world-overlay registration.</summary>
    private void UpdateOverlayRegistration()
    {
        bool active = showPlayers || showNpcs || showProjectiles || showSwings;
        if (!active)
        {
            overlayRegistration?.Dispose();
            overlayRegistration = null;
            return;
        }

        if ((overlayRegistration == null || overlayRegistration.IsReleased) && overlays != null)
        {
            overlayRegistration = overlays.Register(new PluginOverlayDescriptor("hitbox-overlay", PluginOverlayLayer.WorldMarkers), DrawOverlay);
        }
    }

    private void DrawOverlay(IPluginOverlayCanvas canvas, PluginOverlayFrame frame)
    {
        if (entities == null || frame.IsGameMenu || (!showPlayers && !showNpcs && !showProjectiles && !showSwings)) return;
        entityBuffer.Clear();
        entities.CopyActiveEntities(entityBuffer);
        for (int index = 0; index < entityBuffer.Count; index++)
        {
            PluginEntitySnapshot entity = entityBuffer[index];
            if (entity.Kind == PluginEntityKind.Player && showPlayers) Draw(canvas, entity, playerColor);
            else if (entity.Kind == PluginEntityKind.Npc && showNpcs) Draw(canvas, entity, npcColor);
            else if (entity.Kind == PluginEntityKind.Projectile && showProjectiles)
            {
                if (entity.Friendly)
                {
                    if (showFriendlyProjectiles) Draw(canvas, entity, friendlyProjectileColor);
                }
                // Terraria also has active neutral projectiles. Enhancer treated them as the
                // default/hostile presentation category, so they must not disappear merely
                // because neither network combat flag is set.
                else if (showHostileProjectiles)
                {
                    Draw(canvas, entity, hostileProjectileColor);
                }
            }
        }
        if (!showSwings) return;
        swingBuffer.Clear();
        entities.CopyMeleeHitboxes(swingBuffer);
        for (int index = 0; index < swingBuffer.Count; index++) Draw(canvas, swingBuffer[index], swingColor);
    }

    private static void Draw(IPluginOverlayCanvas canvas, PluginEntitySnapshot entity, PluginColor color)
    {
        if (entity.Width <= 0f || entity.Height <= 0f) return;
        canvas.DrawWorldRectangle(entity.X, entity.Y, entity.Width, entity.Height, new PluginOverlayColor(color.Red, color.Green, color.Blue, 220), 2f);
    }

}
