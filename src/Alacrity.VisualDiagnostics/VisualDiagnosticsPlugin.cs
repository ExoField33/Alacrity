using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.VisualDiagnostics;

/// <summary>Local-only diagnostics built entirely on reusable game-state snapshots and world overlays.</summary>
public sealed class VisualDiagnosticsPlugin : IAlacrityPlugin
{
    private readonly List<PluginNpcTargetSnapshot> targets = new List<PluginNpcTargetSnapshot>(64);
    private readonly List<PluginWorldSectionSnapshot> sections = new List<PluginWorldSectionSnapshot>(32);
    private IPluginNpcTargetSnapshotService? npcTargets;
    private IPluginWorldSectionService? worldSections;
    private IPluginOverlayService? overlays;
    private IPluginRegistration? overlayRegistration;
    private IPluginSetting<bool>? showAggroLinesSetting;
    private IPluginSetting<bool>? showSectionsSetting;
    private IPluginSetting<string>? aggroColorSetting;
    private IPluginSetting<string>? bossAggroColorSetting;
    private IPluginSetting<int>? aggroThicknessSetting;
    private bool showAggroLines;
    private bool showSections;
    private PluginColor aggroColor = new PluginColor(255, 90, 90);
    private PluginColor bossAggroColor = new PluginColor(255, 150, 60);
    private int aggroThickness = 2;

    public void Initialize(IPluginContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        npcTargets = context.Terraria.NpcTargets;
        worldSections = context.Terraria.WorldSections;
        overlays = context.Overlays;

        showAggroLinesSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showHostileNpcAggroLines", false));
        showSectionsSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showTileSections", false));
        aggroColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("hostileNpcAggroLineColor", aggroColor.ToHex()));
        bossAggroColorSetting = context.Settings.Register(new PluginSettingDefinition<string>("bossAggroLineColor", bossAggroColor.ToHex()));
        aggroThicknessSetting = context.Settings.Register(new PluginSettingDefinition<int>("aggroLineThickness", 2, value => Clamp(value, 1, 10)));
        ReadSettings();

        showAggroLinesSetting.Subscribe(value => { showAggroLines = value; UpdateOverlayRegistration(); });
        showSectionsSetting.Subscribe(value => { showSections = value; UpdateOverlayRegistration(); });
        aggroColorSetting.Subscribe(value => aggroColor = ReadColor(value, aggroColor));
        bossAggroColorSetting.Subscribe(value => bossAggroColor = ReadColor(value, bossAggroColor));
        aggroThicknessSetting.Subscribe(value => aggroThickness = Clamp(value, 1, 10));

        context.Ui.RegisterSettingsPage(new PluginUiContribution("visual-diagnostics", "Visual Diagnostics"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("hostile-npc-aggro-lines", "Hostile NPC Aggro Lines", showAggroLinesSetting).InPage("visual-diagnostics"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("tile-section-overlay", "Tile Section Overlay", showSectionsSetting).InPage("visual-diagnostics"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("aggro-line-color", "Aggro Line Color", aggroColorSetting, aggroColor).InPage("visual-diagnostics"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Color("boss-aggro-line-color", "Boss Aggro Line Color", bossAggroColorSetting, bossAggroColor).InPage("visual-diagnostics"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("aggro-line-thickness", "Aggro Line Thickness", 1f, 10f, 1f, aggroThicknessSetting, value => ((int)Math.Round(value)).ToString()).InPage("visual-diagnostics"));
        UpdateOverlayRegistration();
    }

    public void Enable() { }

    public void Disable()
    {
        targets.Clear();
        sections.Clear();
        overlayRegistration?.Dispose();
        overlayRegistration = null;
        overlays = null;
        npcTargets = null;
        worldSections = null;
    }

    public void Shutdown()
    {
        Disable();
        showAggroLinesSetting = null;
        showSectionsSetting = null;
        aggroColorSetting = null;
        bossAggroColorSetting = null;
        aggroThicknessSetting = null;
    }

    private void ReadSettings()
    {
        showAggroLines = showAggroLinesSetting!.Value;
        showSections = showSectionsSetting!.Value;
        aggroColor = ReadColor(aggroColorSetting!.Value, aggroColor);
        bossAggroColor = ReadColor(bossAggroColorSetting!.Value, bossAggroColor);
        aggroThickness = Clamp(aggroThicknessSetting!.Value, 1, 10);
    }

    /// <summary>Default-off diagnostics do not retain a world-overlay callback.</summary>
    private void UpdateOverlayRegistration()
    {
        if (!showAggroLines && !showSections)
        {
            overlayRegistration?.Dispose();
            overlayRegistration = null;
            return;
        }

        if ((overlayRegistration == null || overlayRegistration.IsReleased) && overlays != null)
        {
            overlayRegistration = overlays.Register(new PluginOverlayDescriptor("world-diagnostics", PluginOverlayLayer.WorldMarkers), Draw);
        }
    }

    private void Draw(IPluginOverlayCanvas canvas, PluginOverlayFrame frame)
    {
        if (frame.IsGameMenu) return;
        if (showAggroLines && npcTargets != null)
        {
            targets.Clear();
            npcTargets.CopyHostileNpcTargets(targets);
            for (int index = 0; index < targets.Count; index++)
            {
                PluginNpcTargetSnapshot target = targets[index];
                PluginColor color = target.IsBoss ? bossAggroColor : aggroColor;
                canvas.DrawWorldLine(target.NpcCenterX, target.NpcCenterY, target.TargetCenterX, target.TargetCenterY, ToOverlayColor(color, target.IsBoss ? (byte)180 : (byte)150), aggroThickness);
            }
        }

        if (!showSections || worldSections == null) return;
        sections.Clear();
        worldSections.CopyVisibleSections(sections, 1);
        for (int index = 0; index < sections.Count; index++)
        {
            PluginWorldSectionSnapshot section = sections[index];
            PluginOverlayColor color = section.IsLoaded
                ? new PluginOverlayColor(80, 255, 120, 125)
                : new PluginOverlayColor(255, 85, 85, 125);
            canvas.DrawWorldRectangle(section.WorldX, section.WorldY, section.WorldWidth, section.WorldHeight, color, 2f);
            canvas.DrawWorldMarker(section.WorldX + 8f, section.WorldY + 8f, (section.IsLoaded ? "Loaded " : "Pending ") + section.SectionX + "," + section.SectionY, color);
        }
    }

    private static PluginColor ReadColor(string value, PluginColor fallback)
    {
        return PluginColor.TryParseHex(value, out PluginColor parsed) ? parsed : fallback;
    }

    private static PluginOverlayColor ToOverlayColor(PluginColor color, byte alpha)
    {
        return new PluginOverlayColor(color.Red, color.Green, color.Blue, alpha);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
