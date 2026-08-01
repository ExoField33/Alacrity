using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alacrity.App;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace AlacrityTerraria;

public static partial class PluginUiRuntime
{
        private sealed partial class PluginSettingsMenu : UIState
        {
            private readonly PluginManagerRow plugin;
            private readonly IReadOnlyList<PluginSettingControl> controls;
            private readonly IReadOnlyList<PluginUiContribution> legacyPages;
            private UIGamepadHelper gamepadHelper;
            private UIList settingsList;

            public PluginSettingsMenu(PluginManagerRow plugin, IReadOnlyList<PluginSettingControl> controls, IReadOnlyList<PluginUiContribution> legacyPages)
            {
                this.plugin = plugin;
                this.controls = controls;
                this.legacyPages = legacyPages;
            }

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
                SetupGamepadPoints(spriteBatch);
            }

            public override void OnInitialize()
            {
                // This deliberately mirrors UIManageControls: Terraria owns the panel, list, slider, and scrollbar visuals.
                var outer = new UIElement { Width = new StyleDimension(0f, 0.8f), MaxWidth = new StyleDimension(600f, 0f), Top = new StyleDimension(220f, 0f), Height = new StyleDimension(-200f, 1f), HAlign = 0.5f };
                Append(outer);
                var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-110f, 1f), BackgroundColor = new Color(33, 43, 79) * 0.8f };
                outer.Append(panel);
                // Plugin controls are registered in their intended display order; default UIList sorting is not stable for equal UI elements.
                var list = new UIList { Width = new StyleDimension(-25f, 1f), Height = new StyleDimension(-50f, 1f), VAlign = 1f, PaddingBottom = 5f, ListPadding = 20f, ManualSortMethod = items => { } };
                settingsList = list;
                panel.Append(list);

                int snapIndex = 0;
                foreach (var control in controls)
                    AddControl(list, control, snapIndex++);
                foreach (var page in legacyPages.Where(contribution => contribution.IsInteractive))
                    AddLegacyControl(list, page, snapIndex++);

                var scrollbar = new UIScrollbar { Height = new StyleDimension(-67f, 1f), HAlign = 1f, VAlign = 1f, MarginBottom = 11f };
                panel.Append(scrollbar);
                list.SetScrollbar(scrollbar);

                var title = new UITextPanel<string>(plugin.Name + " Settings", 0.7f, true) { HAlign = 0.5f, Top = new StyleDimension(-45f, 0f), Left = new StyleDimension(-10f, 0f), BackgroundColor = new Color(73, 94, 171) };
                title.SetPadding(15f);
                outer.Append(title);
                var back = new UITextPanel<string>("Back", 0.7f, true) {
                    Width = new StyleDimension(-8f, 0.5f), Height = new StyleDimension(50f, 0f),
                    HAlign = 0.5f, VAlign = 1f, Top = new StyleDimension(-20f, 0f)
                };
                back.OnMouseOver += (evt, element) => {
                    var button = (UIPanel)element;
                    button.BackgroundColor = new Color(73, 94, 171);
                    button.BorderColor = Colors.FancyUIFatButtonMouseOver;
                };
                back.OnMouseOut += (evt, element) => {
                    var button = (UIPanel)element;
                    button.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                    button.BorderColor = Color.Black;
                };
                back.OnLeftClick += (evt, element) => ReturnToPluginList();
                back.SetSnapPoint("GoBack", 0, null, null);
                outer.Append(back);
            }

            private static void AddControl(UIList list, PluginSettingControl control, int snapIndex)
            {
                if (control.Kind == PluginSettingControlKind.Slider)
                {
                    var slider = new UIKeybindingSliderItem(
                        () => control.DisplayName + ": " + ReadSettingValue(control),
                        () => Normalize(control.GetSlider(), control.Minimum, control.Maximum),
                        value => control.SetSlider(Denormalize(value, control.Minimum, control.Maximum, control.Step)),
                        () => { }, control.Id.GetHashCode(), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(34f, 0f) };
                    slider.SetSnapPoint("PluginSetting", snapIndex, null, null);
                    list.Add(slider);
                    return;
                }
                if (control.Kind == PluginSettingControlKind.Color)
                {
                    AddColorControls(list, control, snapIndex);
                    return;
                }
                var entry = new UIKeybindingSimpleListItem(() => control.DisplayName + ": " + ReadSettingValue(control), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(30f, 0f) };
                entry.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                entry.OnLeftClick += (evt, element) => ActivateSetting(control);
                entry.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(entry);
            }

            private static void AddLegacyControl(UIList list, PluginUiContribution contribution, int snapIndex)
            {
                var entry = new UIKeybindingSimpleListItem(() => contribution.DisplayName + ": " + ReadSettingValue(contribution), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(30f, 0f) };
                entry.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                entry.OnLeftClick += (evt, element) => ActivateSetting(contribution);
                entry.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(entry);
            }

            private static void AddColorControls(UIList list, PluginSettingControl control, int snapIndex)
            {
                var row = new UIKeybindingSimpleListItem(() => control.DisplayName + ": " + control.GetColor().ToHex(), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(38f, 0f) };
                var swatch = new UIPanel { Width = new StyleDimension(20f, 0f), Height = new StyleDimension(20f, 0f), HAlign = 1f, VAlign = 0.5f, Left = new StyleDimension(-72f, 0f), IgnoresMouseInteraction = true };
                swatch.OnUpdate += element => ((UIPanel)element).BackgroundColor = new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue);
                row.Append(swatch);
                row.Append(CreateClipboardIcon("Images/UI/CharCreation/Copy", -46f, "Copy color hex", () => TrySetClipboardText(control.GetColor().ToHex())));
                row.Append(CreateClipboardIcon("Images/UI/CharCreation/Paste", -22f, "Paste color hex", () => { if (PluginColor.TryParseHex(TryGetClipboardText(), out var value)) control.SetColor(value); }));
                row.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(row);
            }

            private static UIElement CreateClipboardIcon(string assetPath, float offset, string hoverText, Action click)
            {
                // 20px is 65% of Terraria's small character-creation button, keeping the action icons inside this compact row.
                var button = new UIPanel { Width = new StyleDimension(20f, 0f), Height = new StyleDimension(20f, 0f) };
                button.SetPadding(0f);
                button.HAlign = 1f;
                button.VAlign = 0.5f;
                button.Left = new StyleDimension(offset, 0f);
                var image = (UIElement)Activator.CreateInstance(typeof(UIImage), PluginSelectionMenu.RequestTexture(assetPath));
                image.Width = StyleDimension.Fill;
                image.Height = StyleDimension.Fill;
                image.IgnoresMouseInteraction = true;
                typeof(UIImage).GetProperty("ScaleToFit", BindingFlags.Public | BindingFlags.Instance)?.SetValue(image, true, null);
                button.Append(image);
                button.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                button.BorderColor = Color.Black;
                button.OnMouseOver += (evt, element) => {
                    var panel = (UIPanel)element;
                    panel.BackgroundColor = new Color(73, 94, 171);
                    panel.BorderColor = Colors.FancyUIFatButtonMouseOver;
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                };
                button.OnMouseOut += (evt, element) => {
                    var panel = (UIPanel)element;
                    panel.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                    panel.BorderColor = Color.Black;
                };
                button.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText(hoverText); };
                button.OnLeftClick += (evt, element) => { click(); SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f); };
                return button;
            }

            private static float Normalize(float value, float min, float max) => MathHelper.Clamp((value - min) / (max - min), 0f, 1f);
            private static float Denormalize(float value, float min, float max, float step)
            {
                float result = min + MathHelper.Clamp(value, 0f, 1f) * (max - min);
                return step <= 0f ? result : min + (float)Math.Round((result - min) / step) * step;
            }

            private void SetupGamepadPoints(SpriteBatch spriteBatch)
            {
                int firstId = 3600;
                int nextId = firstId;
                List<SnapPoint> allPoints = GetSnapPoints();
                List<SnapPoint> visibleSettings = settingsList.GetSnapPoints();
                gamepadHelper.CullPointsOutOfElementArea(spriteBatch, visibleSettings, settingsList);
                UILinkPoint[] settings = gamepadHelper.CreateUILinkStripVertical(ref nextId, gamepadHelper.GetOrderedPointsByCategoryName(visibleSettings, "PluginSetting"));
                UILinkPoint back = null;
                foreach (SnapPoint point in allPoints)
                    if (point.Name == "GoBack") back = gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                gamepadHelper.LinkVerticalStripBottomSideToSingle(settings, back);
                gamepadHelper.MoveToVisuallyClosestPoint(firstId, nextId);
            }

        }
}
