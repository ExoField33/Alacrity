using System;
using Alacrity.PluginSdk;

namespace Alacrity.Hitboxes;

/// <summary>Renders host-provided collision snapshots through the framework-neutral overlay service.</summary>
public sealed class HitboxesPlugin : IAlacrityPlugin
{
    private IPluginContext? context;
    private IPluginEntitySnapshotService? entities;
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
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        entities = context.Terraria.Entities;
        showPlayers = context.Settings.Get("showPlayerHitboxes", false);
        showNpcs = context.Settings.Get("showNpcHitboxes", false);
        showProjectiles = context.Settings.Get("showProjectileHitboxes", false);
        showFriendlyProjectiles = context.Settings.Get("showFriendlyProjectileHitboxes", true);
        showHostileProjectiles = context.Settings.Get("showHostileProjectileHitboxes", true);
        showSwings = context.Settings.Get("showSwingHitboxes", false);
        playerColor = ReadColor("playerHitboxColor", playerColor);
        npcColor = ReadColor("npcHitboxColor", npcColor);
        friendlyProjectileColor = ReadColor("friendlyProjectileHitboxColor", friendlyProjectileColor);
        hostileProjectileColor = ReadColor("hostileProjectileHitboxColor", hostileProjectileColor);
        swingColor = ReadColor("swingHitboxColor", swingColor);

        context.Ui.RegisterSettingsPage(new PluginUiContribution("hitboxes", "Hitboxes"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("player-hitboxes", "Player Hitboxes", () => showPlayers, value => Set("showPlayerHitboxes", value, ref showPlayers)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("npc-hitboxes", "NPC Hitboxes", () => showNpcs, value => Set("showNpcHitboxes", value, ref showNpcs)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("projectile-hitboxes", "Projectile Hitboxes", () => showProjectiles, value => Set("showProjectileHitboxes", value, ref showProjectiles)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("friendly-projectile-hitboxes", "Friendly Projectile Hitboxes", () => showFriendlyProjectiles, value => Set("showFriendlyProjectileHitboxes", value, ref showFriendlyProjectiles)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("hostile-projectile-hitboxes", "Hostile Projectile Hitboxes", () => showHostileProjectiles, value => Set("showHostileProjectileHitboxes", value, ref showHostileProjectiles)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("swing-hitboxes", "Swing Hitboxes", () => showSwings, value => Set("showSwingHitboxes", value, ref showSwings)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("player-hitbox-color", "Player Hitbox Color", () => playerColor, value => SetColor("playerHitboxColor", value, ref playerColor)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("npc-hitbox-color", "NPC Hitbox Color", () => npcColor, value => SetColor("npcHitboxColor", value, ref npcColor)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("friendly-projectile-hitbox-color", "Friendly Projectile Color", () => friendlyProjectileColor, value => SetColor("friendlyProjectileHitboxColor", value, ref friendlyProjectileColor)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("hostile-projectile-hitbox-color", "Hostile Projectile Color", () => hostileProjectileColor, value => SetColor("hostileProjectileHitboxColor", value, ref hostileProjectileColor)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("swing-hitbox-color", "Swing Hitbox Color", () => swingColor, value => SetColor("swingHitboxColor", value, ref swingColor)));
        context.Overlays.Register(new PluginOverlayDescriptor("hitbox-overlay", PluginOverlayLayer.WorldMarkers), DrawOverlay);
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown()
    {
        entityBuffer.Clear();
        swingBuffer.Clear();
        entities = null;
        context = null;
    }

    private PluginColor ReadColor(string key, PluginColor fallback)
    {
        string stored = context!.Settings.Get(key, fallback.ToHex());
        return PluginColor.TryParseHex(stored, out PluginColor parsed) ? parsed : fallback;
    }

    private void Set(string key, bool value, ref bool field)
    {
        if (field == value) return;
        field = value;
        context?.Settings.Set(key, value);
    }

    private void SetColor(string key, PluginColor value, ref PluginColor field)
    {
        if (field.Equals(value)) return;
        field = value;
        context?.Settings.Set(key, value.ToHex());
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
                if (entity.Friendly && showFriendlyProjectiles) Draw(canvas, entity, friendlyProjectileColor);
                else if (entity.Hostile && showHostileProjectiles) Draw(canvas, entity, hostileProjectileColor);
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
