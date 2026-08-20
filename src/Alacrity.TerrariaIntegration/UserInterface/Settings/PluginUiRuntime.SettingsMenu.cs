using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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
            private UIElement outer;
            private UIElement dropdownLayer;
            private UserInterface.PluginSearchTextElement dropdownSearch;

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

            public override void Update(GameTime gameTime)
            {
                FocusDropdownSearchOnKeyboardInput();
                base.Update(gameTime);
            }

            public override void OnInitialize()
            {
                // This deliberately mirrors UIManageControls: Terraria owns the panel, list, slider, and scrollbar visuals.
                outer = new UIElement { Width = new StyleDimension(0f, 0.8f), MaxWidth = new StyleDimension(600f, 0f), Top = new StyleDimension(220f, 0f), Height = new StyleDimension(-200f, 1f), HAlign = 0.5f };
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

            private void AddControl(UIList list, PluginSettingControl control, int snapIndex)
            {
                if (control.Kind == PluginSettingControlKind.Dropdown)
                {
                    var dropdown = new UIKeybindingSimpleListItem(
                        () => control.DisplayName + ": " + ReadSettingValue(control) + "  >",
                        new Color(73, 94, 171, 255) * 0.9f)
                    {
                        Width = StyleDimension.Fill,
                        Height = new StyleDimension(30f, 0f)
                    };
                    dropdown.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                    dropdown.OnLeftClick += (evt, element) => OpenDropdown(control);
                    dropdown.SetSnapPoint("PluginSetting", snapIndex, null, null);
                    list.Add(dropdown);
                    return;
                }

                if (control.Kind == PluginSettingControlKind.Slider)
                {
                    var slider = new UIKeybindingSliderItem(
                        () => control.DisplayName + ": " + ReadSettingValue(control),
                        () => Normalize(control.GetSlider(), control.Minimum, control.Maximum),
                        value => SetMenuSliderValue(control, value),
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

            private void OpenDropdown(PluginSettingControl control)
            {
                IReadOnlyList<PluginSettingOption> options;
                try
                {
                    options = control.GetDropdownOptions();
                }
                catch (Exception exception)
                {
                    ShowHoverText("Unable to open plugin setting choices: " + exception.Message);
                    return;
                }

                if (options == null || options.Count == 0)
                {
                    ShowHoverText("No choices are available for this setting.");
                    return;
                }

                CloseDropdown(playSound: false);
                dropdownLayer = new UIElement
                {
                    Width = StyleDimension.Fill,
                    Height = StyleDimension.Fill
                };
                // Only clicks outside the panel dismiss the chooser. Child clicks bubble through
                // this layer, so closing unconditionally would make a text search impossible.
                dropdownLayer.OnLeftClick += (evt, element) =>
                {
                    if (evt.Target == dropdownLayer)
                    {
                        CloseDropdown(playSound: true);
                    }
                };
                outer.Append(dropdownLayer);

                var panel = new UIPanel
                {
                    Width = new StyleDimension(0f, 0.78f),
                    Height = new StyleDimension(300f, 0f),
                    HAlign = 0.5f,
                    VAlign = 0.5f,
                    BackgroundColor = new Color(33, 43, 79) * 0.96f
                };
                dropdownLayer.Append(panel);
                var title = new UITextPanel<string>("< " + control.DisplayName, 0.7f, true)
                {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(36f, 0f),
                    BackgroundColor = new Color(73, 94, 171)
                };
                title.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                title.OnLeftClick += (evt, element) => CloseDropdown(playSound: true);
                panel.Append(title);

                var list = new UIList
                {
                    Width = new StyleDimension(-25f, 1f),
                    Height = new StyleDimension(-92f, 1f),
                    Top = new StyleDimension(88f, 0f),
                    ListPadding = 4f,
                    ManualSortMethod = items => { }
                };
                panel.Append(list);

                var searchFrame = new UIPanel
                {
                    Width = new StyleDimension(-12f, 1f),
                    Height = new StyleDimension(30f, 0f),
                    Left = new StyleDimension(6f, 0f),
                    Top = new StyleDimension(50f, 0f),
                    BackgroundColor = new Color(38, 52, 94) * 0.94f,
                    BorderColor = ResourcePackBorder
                };
                searchFrame.SetPadding(2f);
                var search = new UserInterface.PluginSearchTextElement("Search...", 0.68f)
                {
                    Width = StyleDimension.Fill,
                    Height = StyleDimension.Fill
                };
                search.ContentsChanged += text => PopulateDropdownOptions(list, control, options, text);
                searchFrame.OnUpdate += element =>
                {
                    var frame = (UIPanel)element;
                    bool focused = search.IsWritingText;
                    frame.BackgroundColor = focused
                        ? new Color(59, 80, 151) * 0.96f
                        : new Color(38, 52, 94) * 0.94f;
                    frame.BorderColor = focused
                        ? Colors.FancyUIFatButtonMouseOver
                        : ResourcePackBorder;
                };
                searchFrame.Append(search);
                dropdownSearch = search;
                panel.Append(searchFrame);
                PopulateDropdownOptions(list, control, options, string.Empty);

                var scrollbar = new UIScrollbar
                {
                    Height = new StyleDimension(-98f, 1f),
                    HAlign = 1f,
                    Top = new StyleDimension(90f, 0f)
                };
                // UIList handles wheel events over list rows. This covers the narrow scrollbar
                // track too, whose native element otherwise only supports dragging.
                scrollbar.OnScrollWheel += (evt, element) => scrollbar.ViewPosition -= evt.ScrollWheelValue;
                panel.Append(scrollbar);
                list.SetScrollbar(scrollbar);
                SoundEngine.PlaySound(10, -1, -1, 1, 1f, 0f);
            }

            private void PopulateDropdownOptions(UIList list, PluginSettingControl control, IReadOnlyList<PluginSettingOption> options, string searchText)
            {
                list.Clear();
                string selected = ReadSettingValue(control);
                int matchCount = 0;
                for (int index = 0; index < options.Count; index++)
                {
                    PluginSettingOption option = options[index];
                    if (!PluginDropdownFilter.Matches(option.DisplayName, option.Value, searchText))
                    {
                        continue;
                    }

                    matchCount++;
                    bool isSelected = string.Equals(option.Value, selected, StringComparison.OrdinalIgnoreCase);
                    var item = new UIKeybindingSimpleListItem(
                        () => isSelected ? option.DisplayName + "  *" : option.DisplayName,
                        new Color(73, 94, 171, 255) * 0.9f)
                    {
                        Width = StyleDimension.Fill,
                        Height = new StyleDimension(28f, 0f)
                    };
                    item.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                    item.OnLeftClick += (evt, element) => SelectDropdownOption(control, option.Value);
                    list.Add(item);
                }

                if (matchCount == 0)
                {
                    var empty = new UIKeybindingSimpleListItem(() => "No matching choices.", new Color(73, 94, 171, 255) * 0.55f)
                    {
                        Width = StyleDimension.Fill,
                        Height = new StyleDimension(28f, 0f),
                        IgnoresMouseInteraction = true
                    };
                    list.Add(empty);
                }
            }

            private void SelectDropdownOption(PluginSettingControl control, string value)
            {
                try
                {
                    control.SetDropdown(value);
                    CloseDropdown(playSound: true);
                }
                catch (Exception exception)
                {
                    ShowHoverText("Unable to change plugin setting: " + exception.Message);
                }
            }

            /// <summary>Closes the host-owned chooser before menu-level Escape changes screens.</summary>
            internal bool TryCloseDropdown()
            {
                if (dropdownLayer == null)
                {
                    return false;
                }

                if (dropdownSearch != null && dropdownSearch.IsWritingText)
                {
                    dropdownSearch.Blur();
                    return true;
                }

                CloseDropdown(playSound: true);
                return true;
            }

            private void CloseDropdown(bool playSound)
            {
                if (dropdownLayer == null)
                {
                    return;
                }

                outer?.RemoveChild(dropdownLayer);
                dropdownLayer = null;
                dropdownSearch = null;
                if (playSound)
                {
                    SoundEngine.PlaySound(11, -1, -1, 1, 1f, 0f);
                }
            }

            private void FocusDropdownSearchOnKeyboardInput()
            {
                if (dropdownSearch == null || dropdownSearch.IsWritingText)
                {
                    return;
                }

                KeyboardState current = Main.keyState;
                KeyboardState previous = Main.oldKeyState;
                if (current.IsKeyDown(Keys.Escape) ||
                    previous.IsKeyDown(Keys.Escape) ||
                    (current.IsKeyDown(Keys.Enter) && !previous.IsKeyDown(Keys.Enter)))
                {
                    return;
                }

                // Start the host-owned search before child elements update so the triggering
                // typed input belongs to the chooser instead of falling through underneath it.
                if (HasPendingDropdownSearchText())
                {
                    dropdownSearch.Focus(preservePendingText: true);
                }
            }

            private static bool HasPendingDropdownSearchText()
            {
                int count = Math.Max(0, Math.Min(Main.keyCount, Main.keyInt.Length));
                for (int index = 0; index < count; index++)
                {
                    int key = Main.keyInt[index];
                    if (key >= 32 && key != 127)
                    {
                        return true;
                    }
                }

                return false;
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
                var swatch = new UIPanel { Width = new StyleDimension(20f, 0f), Height = new StyleDimension(20f, 0f), HAlign = 1f, VAlign = 0.5f, Left = new StyleDimension(-84f, 0f), Top = new StyleDimension(-3f, 0f), IgnoresMouseInteraction = true };
                swatch.OnUpdate += element => ((UIPanel)element).BackgroundColor = new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue);
                row.Append(swatch);
                row.Append(CreateClipboardIcon(
                    "Images/UI/CharCreation/Copy",
                    -52f,
                    () => "Copy color hex (" + control.GetColor().ToHex() + ")",
                    () => TrySetClipboardText(control.GetColor().ToHex())));
                row.Append(CreateClipboardIcon(
                    "Images/UI/CharCreation/Paste",
                    -22f,
                    GetColorPasteTooltip,
                    () => { if (PluginColor.TryParseHex(TryGetClipboardText(), out var value)) control.SetColor(value); }));
                row.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(row);
            }

            private static UIElement CreateClipboardIcon(string assetPath, float offset, Func<string> getHoverText, Action click)
            {
                // Give the compact action icon the same visual breathing room as Terraria's
                // character-creation buttons while keeping its 20px glyph at native size.
                var button = new UIPanel { Width = new StyleDimension(26f, 0f), Height = new StyleDimension(26f, 0f) };
                button.SetPadding(0f);
                button.HAlign = 1f;
                button.VAlign = 0.5f;
                button.Top = new StyleDimension(-3f, 0f);
                button.Left = new StyleDimension(offset, 0f);
                var image = (UIElement)Activator.CreateInstance(typeof(UIImage), PluginSelectionMenu.RequestTexture(assetPath));
                image.Width = new StyleDimension(20f, 0f);
                image.Height = new StyleDimension(20f, 0f);
                image.HAlign = 0.5f;
                image.VAlign = 0.5f;
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
                button.OnUpdate += element => { if (element.IsMouseHovering) ShowHoverText(getHoverText()); };
                button.OnLeftClick += (evt, element) => { click(); SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f); };
                return button;
            }

            private static float Normalize(float value, float min, float max) => MathHelper.Clamp((value - min) / (max - min), 0f, 1f);
            private static float Denormalize(float value, float min, float max, float step)
            {
                float result = min + MathHelper.Clamp(value, 0f, 1f) * (max - min);
                return step <= 0f ? result : min + (float)Math.Round((result - min) / step) * step;
            }

            private static void SetMenuSliderValue(PluginSettingControl control, float normalizedValue)
            {
                float current = control.GetSlider();
                float next = Denormalize(normalizedValue, control.Minimum, control.Maximum, control.Step);
                if (Math.Abs(current - next) <= 0.0001f)
                    return;

                control.SetSlider(next);
                if (control.Step > 0f)
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
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
