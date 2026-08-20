using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Alacrity.Core;
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
        internal sealed partial class PluginSelectionMenu : UIState
        {
            private readonly UIList _availableList = new UIList();
            private readonly UIList _enabledList = new UIList();
            private readonly UIList _packageList = new UIList();
            private readonly UIText _availableTitle = new UIText("", 1f, false);
            private readonly UIText _enabledTitle = new UIText("", 1f, false);
            private readonly UIText _packageTitle = new UIText("", 1f, false);
            private UIGamepadHelper _gamepadHelper;
            private UIText _settingsHint;
            private DateTime _nextStatusRefreshUtc;
            private DateTime _manualHintExpiresUtc;
            private bool _addRemoveView;

            public PluginSelectionMenu(PluginManagementMenu menu)
            {
                BuildPage();
            }

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                RefreshRuntimeStatusHint(false);
                SetupGamepadPoints(spriteBatch);
            }

            private void BuildPage()
            {
                RemoveAllChildren();
                var root = new UIElement {
                    Width = new StyleDimension(0f, 0.8f),
                    MaxWidth = new StyleDimension(800f, 0f),
                    MinWidth = new StyleDimension(600f, 0f),
                    Top = new StyleDimension(240f, 0f),
                    Height = new StyleDimension(-240f, 1f),
                    HAlign = 0.5f
                };
                Append(root);

                var panel = new UIPanel {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(-110f, 1f),
                    BackgroundColor = new Color(33, 43, 79) * 0.8f,
                    PaddingLeft = 0f,
                    PaddingRight = 0f
                };
                root.Append(panel);

                var listArea = new UIElement {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(-39f, 1f),
                    VAlign = 1f
                };
                listArea.SetPadding(0f);
                panel.Append(listArea);

                if (_addRemoveView)
                {
                    var packageContainer = new UIElement { Width = new StyleDimension(-20f, 1f), Height = StyleDimension.Fill, HAlign = 0.5f };
                    listArea.Append(packageContainer);
                    ConfigureList(_packageList, 0.5f);
                    packageContainer.Append(_packageList);
                    _packageTitle.HAlign = 0.5f;
                    _packageTitle.Width = new StyleDimension(-25f, 1f);
                    _packageTitle.Top = new StyleDimension(10f, 0f);
                    panel.Append(_packageTitle);
                    AddScrollbar(packageContainer, _packageList, 1f);
                }
                else
                {
                    var availableContainer = CreateColumn(0f, 10f);
                    var enabledContainer = CreateColumn(1f, -10f);
                    listArea.Append(availableContainer);
                    listArea.Append(enabledContainer);
                    ConfigureList(_availableList, 1f);
                    ConfigureList(_enabledList, 0f);
                    availableContainer.Append(_availableList);
                    enabledContainer.Append(_enabledList);
                    ConfigureTitle(_availableTitle, 0f, 25f);
                    ConfigureTitle(_enabledTitle, 1f, -25f);
                    panel.Append(_availableTitle);
                    panel.Append(_enabledTitle);
                    AddScrollbar(availableContainer, _availableList, 0f);
                    AddScrollbar(enabledContainer, _enabledList, 1f);
                    AddSeparator(panel);
                }

                var title = new UITextPanel<string>("Plugins", 1f, true) {
                    HAlign = 0.5f,
                    VAlign = 0f,
                    Top = new StyleDimension(-44f, 0f),
                    BackgroundColor = new Color(73, 94, 171)
                };
                title.SetPadding(13f);
                root.Append(title);

                AddBottomControls(root);
                RefreshLists();
            }

            private static UIElement CreateColumn(float align, float offset)
            {
                var column = new UIElement {
                    Width = new StyleDimension(-20f, 0.5f),
                    Height = StyleDimension.Fill,
                    HAlign = align,
                    Left = new StyleDimension(offset, 0f)
                };
                column.SetPadding(0f);
                return column;
            }

            private static void ConfigureList(UIList list, float align)
            {
                list.Width = new StyleDimension(-25f, 1f);
                list.Height = StyleDimension.Fill;
                list.ListPadding = 5f;
                list.HAlign = align;
                // Rows are already ordered by PluginManagerPresenter. UIList's default sort is
                // not stable for equal-priority panels and can scramble that display order.
                list.ManualSortMethod = items => { };
            }

            private static void ConfigureTitle(UIText title, float align, float offset)
            {
                title.HAlign = align;
                title.Left = new StyleDimension(offset, 0f);
                title.Width = new StyleDimension(-25f, 0.5f);
                title.VAlign = 0f;
                title.Top = new StyleDimension(10f, 0f);
            }

            private static void AddScrollbar(UIElement container, UIList list, float align)
            {
                var scrollbar = new UIScrollbar((UIScrollbar.ColorTheme)0) {
                    Height = StyleDimension.Fill,
                    HAlign = align
                };
                container.Append(scrollbar);
                list.SetScrollbar(scrollbar);
            }

            private static void AddSeparator(UIPanel panel)
            {
                var separator = new UIVerticalSeparator {
                    Height = new StyleDimension(-12f, 1f),
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Color = new Color(89, 116, 213, 255) * 0.9f
                };
                panel.Append(separator);
            }

            private void AddBottomControls(UIElement root)
            {
                var footer = new UIElement { Width = StyleDimension.Fill, Height = new StyleDimension(50f, 0f), VAlign = 1f, Top = new StyleDimension(-45f, 0f) };
                root.Append(footer);
                var back = CreateFooterButton("Back", 0.7f, false, Close);
                PlaceFooterButton(back, 0);
                back.SetSnapPoint("GoBack", 0);
                footer.Append(back);

                var manage = CreateFooterButton("Manage Plugins", 0.48f, !_addRemoveView, () => { _addRemoveView = false; BuildPage(); });
                PlaceFooterButton(manage, 1);
                manage.SetSnapPoint("ManagePlugins", 0);
                footer.Append(manage);
                var addRemove = CreateFooterButton("Add / Remove Plugins", 0.34f, _addRemoveView, () => { _addRemoveView = true; BuildPage(); });
                PlaceFooterButton(addRemove, 2);
                addRemove.SetSnapPoint("AddRemovePlugins", 0);
                footer.Append(addRemove);

                var folder = CreateFooterButton("Open Folder", 0.48f, false, OpenPluginsFolder);
                PlaceFooterButton(folder, 3);
                folder.SetSnapPoint("OpenFolder", 0);
                footer.Append(folder);

                _settingsHint = new UIText("", 0.7f, false) {
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Top = new StyleDimension(-160f, 0f)
                };
                root.Append(_settingsHint);
            }

            private static void PlaceFooterButton(UIElement button, int column)
            {
                var width = new StyleDimension(-12f, 0.25f);
                button.Width = width;
                button.MinWidth = width;
                button.MaxWidth = width;
                button.Height = StyleDimension.Fill;
                button.Left = new StyleDimension(6f, column * 0.25f);
                button.HAlign = 0f;
                button.VAlign = 0f;
                button.OverflowHidden = true;
            }

            private static UITextPanel<string> CreateFooterButton(string text, float textScale, bool selected, Action activate)
            {
                var button = new UITextPanel<string>(text, textScale, large: true) {
                    BackgroundColor = selected ? new Color(73, 94, 171) : new Color(63, 82, 151) * 0.8f,
                    OverflowHidden = true
                };
                button.SetPadding(12f);
                button.OnMouseOver += (evt, element) => FadedMouseOver((UIPanel)element);
                button.OnMouseOut += (evt, element) => {
                    var panel = (UIPanel)element;
                    if (selected) {
                        panel.BackgroundColor = new Color(73, 94, 171);
                        panel.BorderColor = Color.Black;
                    }
                    else FadedMouseOut(panel);
                };
                button.OnLeftClick += (evt, element) => activate();
                return button;
            }

            private static void FadedMouseOver(UIPanel panel)
            {
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                panel.BackgroundColor = new Color(73, 94, 171);
                panel.BorderColor = Colors.FancyUIFatButtonMouseOver;
            }

            private static void FadedMouseOut(UIPanel panel)
            {
                panel.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                panel.BorderColor = Color.Black;
            }

            private static void OpenPluginsFolder()
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                Directory.CreateDirectory(path);
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                Utils.OpenFolder(path);
            }

            private void SetupGamepadPoints(SpriteBatch spriteBatch)
            {
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
                int startId = 3000;
                int nextId = startId;
                var allPoints = GetSnapPoints();

                if (_addRemoveView)
                {
                    SetupPackageGamepadPoints(spriteBatch, allPoints, startId, ref nextId);
                    return;
                }

                var availablePoints = _availableList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, availablePoints, _availableList);
                var enabledPoints = _enabledList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, enabledPoints, _enabledList);

                var availableDescription = GetVisualVerticalStrip(ref nextId, availablePoints, "DescriptionOff");
                var availableToggle = GetVisualVerticalStrip(ref nextId, availablePoints, "ToggleToOn");
                var enabledDescription = GetVisualVerticalStrip(ref nextId, enabledPoints, "DescriptionOn");
                var enabledSettings = GetVisualVerticalStrip(ref nextId, enabledPoints, "SettingsOn");
                var enabledToggle = GetVisualVerticalStrip(ref nextId, enabledPoints, "ToggleToOff");
                GetFooterLinkPoints(allPoints, ref nextId, out UILinkPoint back, out UILinkPoint manage, out UILinkPoint addRemove, out UILinkPoint folder);

                // Disabled plugins intentionally have no settings action. Link their two actual actions directly.
                _gamepadHelper.LinkVerticalStrips(availableDescription, availableToggle, 0);
                _gamepadHelper.LinkVerticalStrips(availableToggle, enabledDescription, 0);
                _gamepadHelper.LinkVerticalStrips(enabledDescription, enabledSettings, 0);
                _gamepadHelper.LinkVerticalStrips(enabledSettings, enabledToggle, 0);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableToggle, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableDescription, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledToggle, folder);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledSettings, folder);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledDescription, folder);
                _gamepadHelper.MoveToVisuallyClosestPoint(startId, nextId);
            }

            // UIList children are independently populated; their snap IDs are not a reliable visual
            // order across the enabled and available columns. Navigator up/down must follow screen Y.
            private UILinkPoint[] GetVisualVerticalStrip(ref int nextId, List<SnapPoint> points, string category)
            {
                var ordered = points
                    .Where(point => point.Name == category)
                    .OrderBy(point => point.Position.Y)
                    .ThenBy(point => point.Position.X)
                    .ThenBy(point => point.Id)
                    .ToList();
                return ordered.Count == 0 ? null : _gamepadHelper.CreateUILinkStripVertical(ref nextId, ordered);
            }

            private void SetupPackageGamepadPoints(SpriteBatch spriteBatch, List<SnapPoint> allPoints, int startId, ref int nextId)
            {
                var packagePoints = _packageList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, packagePoints, _packageList);
                var description = GetVisualVerticalStrip(ref nextId, packagePoints, "PackageDescription");
                var uninstall = GetVisualVerticalStrip(ref nextId, packagePoints, "PackageUninstall");
                GetFooterLinkPoints(allPoints, ref nextId, out UILinkPoint back, out UILinkPoint manage, out UILinkPoint addRemove, out UILinkPoint folder);

                _gamepadHelper.LinkVerticalStrips(description, uninstall, 0);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(description, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(uninstall, folder);
                LinkFooterStrip(back, manage, addRemove, folder);
                _gamepadHelper.MoveToVisuallyClosestPoint(startId, nextId);
            }

            private void GetFooterLinkPoints(List<SnapPoint> allPoints, ref int nextId, out UILinkPoint back, out UILinkPoint manage, out UILinkPoint addRemove, out UILinkPoint folder)
            {
                back = null;
                manage = null;
                addRemove = null;
                folder = null;
                foreach (SnapPoint point in allPoints)
                {
                    if (point.Name == "GoBack")
                        back = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "ManagePlugins")
                        manage = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "AddRemovePlugins")
                        addRemove = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "OpenFolder")
                        folder = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                }

                LinkFooterStrip(back, manage, addRemove, folder);
            }

            private void LinkFooterStrip(UILinkPoint back, UILinkPoint manage, UILinkPoint addRemove, UILinkPoint folder)
            {
                _gamepadHelper.PairLeftRight(back, manage);
                _gamepadHelper.PairLeftRight(manage, addRemove);
                _gamepadHelper.PairLeftRight(addRemove, folder);
            }

            private void RefreshLists()
            {
                _availableList.Clear();
                _enabledList.Clear();
                _packageList.Clear();
                int order = 0;
                foreach (PluginManagerRow plugin in _presenter.Present(_runtime, _diagnostics.ActiveWarnings))
                {
                    if (_addRemoveView)
                    {
                        _packageList.Add(CreatePackageRow(plugin, order++));
                        continue;
                    }
                    UIElement row = CreatePluginRow(plugin, order++);
                    if (plugin.IsEnabled)
                        _enabledList.Add(row);
                    else
                        _availableList.Add(row);
                }

                _availableTitle.SetText("Available Plugins (" + _availableList.Count + ")");
                _enabledTitle.SetText("Enabled Plugins (" + _enabledList.Count + ")");
                _packageTitle.SetText("Installed Plugins (" + _packageList.Count + ")");
            }

            private UIElement CreatePackageRow(PluginManagerRow plugin, int order)
            {
                var row = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(92f, 0f), BackgroundColor = ResourcePackBackground, BorderColor = ResourcePackBorder };
                row.SetPadding(5f);
                AppendDependencyBadge(row, plugin);
                var name = new UIText(plugin.Name, 0.9f, false) { Left = new StyleDimension(12f, 0f), Top = new StyleDimension(4f, 0f) };
                var author = new UIText(plugin.Author, 0.65f, false) { Left = new StyleDimension(12f, 0f), Top = new StyleDimension(28f, 0f) };
                row.Append(name); row.Append(author);
                var content = new UIElement { Left = new StyleDimension(12f, 0f), Top = new StyleDimension(48f, 0f), Width = new StyleDimension(-24f, 1f), Height = new StyleDimension(-53f, 1f) };
                row.Append(content);
                var description = CreateDescriptionButton(plugin); description.Width = new StyleDimension(0f, plugin.IsEnabled ? 0.5f : 1f / 3f); description.Height = StyleDimension.Fill; description.SetSnapPoint("PackageDescription", order, null, null); description.OnLeftClick += (evt, element) => OpenDescription(plugin); content.Append(description);
                var status = CreatePackageStatusButton(); status.Left = StyleDimension.FromPercent(plugin.IsEnabled ? 0.5f : 1f / 3f); status.Width = new StyleDimension(0f, plugin.IsEnabled ? 0.5f : 1f / 3f); status.Height = StyleDimension.Fill; content.Append(status);
                if (!plugin.IsEnabled)
                {
                    var uninstall = CreateUninstallButton(); uninstall.Left = StyleDimension.FromPercent(2f / 3f); uninstall.Width = new StyleDimension(0f, 1f / 3f); uninstall.Height = StyleDimension.Fill; uninstall.SetSnapPoint("PackageUninstall", order, null, null); content.Append(uninstall);
                }
                return row;
            }

            private static UIResourcePackInfoButton<string> CreatePackageStatusButton()
            {
                var button = new UIResourcePackInfoButton<string>("", 0.8f, false) { IgnoresMouseInteraction = true };
                button.SetPadding(0f); AppendSmallIcon(button, "Images/UI/ButtonCloudInactive"); button.OnUpdate += element => { if (element.IsMouseHovering) ShowHoverText("Plugin is up to date"); }; return button;
            }

            private static UIResourcePackInfoButton<string> CreateUninstallButton()
            {
                var button = new UIResourcePackInfoButton<string>("", 0.8f, false); button.SetPadding(0f); AppendSmallIcon(button, "Images/UI/ButtonDelete"); button.OnUpdate += element => { if (element.IsMouseHovering) ShowHoverText("Uninstall Plugin"); }; return button;
            }

            private static void AppendSmallIcon(UIElement button, string path)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture(path));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch (Exception exception)
                {
                    ReportOptionalUiFailure("Create plugin-manager icon", exception);
                }
            }

            private UIElement CreatePluginRow(PluginManagerRow plugin, int order)
            {
                var row = new UIPanel {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(102f, 0f),
                    MinHeight = new StyleDimension(102f, 0f),
                    MaxHeight = new StyleDimension(102f, 0f),
                    BackgroundColor = ResourcePackBackground,
                    BorderColor = ResourcePackBorder
                };
                row.SetPadding(5f);
                row.OverflowHidden = true;
                AppendDependencyBadge(row, plugin);
                row.OnMouseOver += (evt, element) => {
                    row.BackgroundColor = ResourcePackHoverBackground;
                    row.BorderColor = ResourcePackHoverBorder;
                };
                row.OnMouseOut += (evt, element) => {
                    row.BackgroundColor = ResourcePackBackground;
                    row.BorderColor = ResourcePackBorder;
                };

                var name = new UIText(plugin.Name, 1f, false) {
                    Left = new StyleDimension(12f, 0f),
                    Top = new StyleDimension(4f, 0f)
                };
                row.Append(name);

                var author = new UIText(plugin.Author, 0.7f, false) {
                    Left = new StyleDimension(12f, 0f),
                    Top = new StyleDimension(30f, 0f)
                };
                row.Append(author);

                var content = new UIElement {
                    Left = new StyleDimension(12f, 0f),
                    Top = new StyleDimension(50f, 0f),
                    Width = new StyleDimension(-24f, 1f),
                    Height = new StyleDimension(-55f, 1f)
                };
                row.Append(content);

                float buttonFraction = plugin.IsEnabled ? 1f / 3f : 0.5f;
                var description = CreateDescriptionButton(plugin);
                description.Width = new StyleDimension(0f, buttonFraction);
                description.Height = StyleDimension.Fill;
                description.SetSnapPoint(plugin.IsEnabled ? "DescriptionOn" : "DescriptionOff", order, null, null);
                description.OnLeftClick += (evt, element) => OpenDescription(plugin);
                content.Append(description);

                if (plugin.IsEnabled)
                {
                    var settings = CreateSettingsButton(plugin);
                    settings.Left = StyleDimension.FromPercent(buttonFraction);
                    settings.Width = new StyleDimension(0f, buttonFraction);
                    settings.Height = StyleDimension.Fill;
                    settings.SetSnapPoint("SettingsOn", order, null, null);
                    settings.OnLeftClick += (evt, element) => OpenSettings(plugin);
                    content.Append(settings);
                }

                var toggle = CreateToggleButton(plugin);
                toggle.Left = StyleDimension.FromPercent(plugin.IsEnabled ? 2f / 3f : 0.5f);
                toggle.Width = new StyleDimension(0f, buttonFraction);
                toggle.Height = StyleDimension.Fill;
                toggle.VAlign = 0f;
                toggle.SetSnapPoint(plugin.IsEnabled ? "ToggleToOff" : "ToggleToOn", order, null, null);
                if (_pluginOperations != null && _pluginOperations.IsPending(plugin.Id))
                {
                    toggle.IgnoresMouseInteraction = true;
                    toggle.SetColorsBasedOnSelectionState(Color.Gray, Color.Gray, 0.55f, 0.55f);
                }
                else if (plugin.CanToggle)
                {
                    toggle.OnLeftClick += (evt, element) => {
                        try
                        {
                            if (!BeginPluginOperation(plugin.Id, !plugin.IsEnabled, out string error))
                            {
                                ShowStatus(error);
                                return;
                            }
                            RefreshRuntimeStatusHint(true);
                        }
                        catch (Exception exception)
                        {
                            _settingsHint.SetText("Unable to change " + plugin.Name + ": " + exception.Message);
                            _nextStatusRefreshUtc = DateTime.MaxValue;
                        }
                        RefreshLists();
                    };
                }
                else
                {
                    toggle.IgnoresMouseInteraction = true;
                    toggle.SetColorsBasedOnSelectionState(Color.Gray, Color.Gray, 0.55f, 0.55f);
                }
                content.Append(toggle);
                return row;
            }

            private void AppendDependencyBadge(UIPanel row, PluginManagerRow plugin)
            {
                var dependencies = _runtime.Registry.Records.FirstOrDefault(record => record.Manifest.Id == plugin.Id)?.Manifest.Dependencies;
                if (dependencies == null || dependencies.Count == 0)
                    return;

                var badge = new UIPanel {
                    Width = new StyleDimension(22f, 0f),
                    Height = new StyleDimension(22f, 0f),
                    HAlign = 1f,
                    VAlign = 0f,
                    Left = new StyleDimension(-5f, 0f),
                    Top = new StyleDimension(5f, 0f)
                };
                badge.SetPadding(2f);
                badge.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                badge.BorderColor = Color.Black;
                try
                {
                    var image = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture("Images/UI/Wires_6"));
                    image.Width = StyleDimension.Fill;
                    image.Height = StyleDimension.Fill;
                    image.IgnoresMouseInteraction = true;
                    typeof(UIImage).GetProperty("ScaleToFit", BindingFlags.Public | BindingFlags.Instance)?.SetValue(image, true, null);
                    badge.Append(image);
                }
                catch (Exception exception)
                {
                    ReportOptionalUiFailure("Create dependency badge", exception);
                    return;
                }

                string tooltip = "Dependencies: \n" + string.Join("\n", dependencies.Select(dependency => {
                    var dependencyRecord = _runtime.Registry.Records.FirstOrDefault(record => record.Manifest.Id == dependency.Id);
                    return "-" + (dependencyRecord == null ? dependency.Id.Value : dependencyRecord.Manifest.Name);
                }));
                badge.OnMouseOver += (evt, element) => {
                    var panel = (UIPanel)element;
                    panel.BackgroundColor = new Color(73, 94, 171);
                    panel.BorderColor = Colors.FancyUIFatButtonMouseOver;
                };
                badge.OnMouseOut += (evt, element) => {
                    var panel = (UIPanel)element;
                    panel.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                    panel.BorderColor = Color.Black;
                };
                badge.OnUpdate += element => { if (element.IsMouseHovering) ShowHoverText(tooltip); };
                row.Append(badge);
            }

            private void OpenSettings(PluginManagerRow plugin)
            {
                if (!HasSettings(plugin.Id))
                {
                    ShowStatus("No settings are exposed by " + plugin.Name + ".");
                    return;
                }
                Main.MenuUI.SetState(new PluginSettingsMenu(plugin, _extensions.GetSettingsControls(plugin.Id), _extensions.GetSettingsPages(plugin.Id)));
            }

            private void RefreshRuntimeStatusHint(bool force)
            {
                if (CompletePluginOperations())
                    RefreshLists();
                var now = DateTime.UtcNow;
                if (now < _manualHintExpiresUtc)
                    return;
                if (!force && now < _nextStatusRefreshUtc)
                    return;

                _nextStatusRefreshUtc = now.AddMilliseconds(250);
                var active = _notifications.GetActive(new DateTimeOffset(now)).Where(notification => (notification.Options.Target & PluginNotificationTarget.PluginManager) != 0).ToArray();
                string text;
                if (active.Length > 0)
                    text = active[active.Length - 1].Message;
                else
                {
                    var warning = _diagnostics.ActiveWarnings.FirstOrDefault();
                    text = warning == null ? string.Empty : warning.Plugin + ": " + warning.Reason;
                }
                _settingsHint.SetText(text);
            }

            private void ShowStatus(string text)
            {
                _settingsHint.SetText(text);
                _manualHintExpiresUtc = DateTime.UtcNow.AddSeconds(4);
            }

            private static GroupOptionButton<bool> CreateToggleButton(PluginManagerRow plugin)
            {
                var toggle = new GroupOptionButton<bool>(
                    true,
                    null,
                    null,
                    Color.White,
                    null,
                    0.8f,
                    0.5f,
                    10f) {
                    ShowHighlightWhenSelected = false
                };
                toggle.SetColorsBasedOnSelectionState(Color.LightGreen, Color.PaleVioletRed, 0.7f, 0.7f);
                toggle.SetCurrentOption(plugin.IsEnabled);
                toggle.SetPadding(0f);

                AppendTexturePackIcon(toggle, plugin.IsEnabled);
                toggle.OnUpdate += (element) => DisplayMouseTextIfHovered(element, plugin.IsEnabled ? "Disable Plugin" : "Enable Plugin");
                return toggle;
            }

            private static UIResourcePackInfoButton<string> CreateDescriptionButton(PluginManagerRow plugin)
            {
                var description = new UIResourcePackInfoButton<string>("", 0.8f, false);
                description.SetPadding(0f);
                AppendPluginDescriptionIcon(description);
                description.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                description.OnUpdate += (element) => DisplayMouseTextIfHovered(element, "Plugin Description");
                return description;
            }

            private static UIResourcePackInfoButton<string> CreateSettingsButton(PluginManagerRow plugin)
            {
                var settings = new UIResourcePackInfoButton<string>("", 0.8f, false);
                settings.SetPadding(0f);
                AppendPluginSettingsIcon(settings);
                settings.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                settings.OnUpdate += (element) => DisplayMouseTextIfHovered(element, "Plugin Settings");
                return settings;
            }

            private static void DisplayMouseTextIfHovered(UIElement element, string text)
            {
                if (element.IsMouseHovering)
                    ShowHoverText(text);
            }

            private static void AppendTexturePackIcon(UIElement button, bool enabled)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(
                        typeof(UIImageFramed),
                        RequestTexture("Images/UI/TexturePackButtons"),
                        GetTextureFrame("Images/UI/TexturePackButtons", 2, 2, enabled ? 0 : 1, 1));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch
                {
                    // The button remains usable if optional artwork cannot be resolved.
                }
            }

            private static void AppendPluginDescriptionIcon(UIElement button)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture("Images/UI/CharCreation/CharInfo"));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch
                {
                    // The button remains usable if optional artwork cannot be resolved.
                }
            }

            private static void AppendPluginSettingsIcon(UIElement button)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture("Images/UI/Camera_1"));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch
                {
                    // The button remains usable if optional artwork cannot be resolved.
                }
            }

            private static void ConfigureButtonIcon(UIElement icon)
            {
                icon.HAlign = 0.5f;
                icon.VAlign = 0.5f;
                icon.IgnoresMouseInteraction = true;
            }

            internal static object RequestTexture(string path)
            {
                object assets = GetMainAssets();
                if (_assetRequest == null)
                {
                    _assetRequest = assets.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method => {
                            ParameterInfo[] parameters = method.GetParameters();
                            return method.Name == "Request" &&
                                   method.IsGenericMethodDefinition &&
                                   parameters.Length == 2 &&
                                   parameters[0].ParameterType == typeof(string) &&
                                   parameters[1].ParameterType.IsEnum;
                        });
                }

                if (_assetRequest == null)
                    throw new MissingMethodException(assets.GetType().FullName, "Request<T>(string, AssetRequestMode)");

                Type modeType = _assetRequest.GetParameters()[1].ParameterType;
                return _assetRequest.MakeGenericMethod(typeof(Texture2D)).Invoke(assets, new object[] { path, Enum.ToObject(modeType, 1) });
            }

            private static Rectangle GetTextureFrame(string path, int horizontalFrames, int verticalFrames, int horizontalFrame, int verticalFrame)
            {
                object asset = RequestTexture(path);
                if (_assetFrame == null)
                {
                    _assetFrame = typeof(Utils).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => {
                            ParameterInfo[] parameters = method.GetParameters();
                            return method.Name == "Frame" &&
                                   parameters.Length == 7 &&
                                   parameters[0].ParameterType.IsGenericType &&
                                   parameters[0].ParameterType.GetGenericTypeDefinition().FullName == "ReLogic.Content.Asset`1";
                        });
                }

                if (_assetFrame == null)
                    throw new MissingMethodException(typeof(Utils).FullName, "Frame(Asset<T>, int, int, int, int, int, int)");

                return (Rectangle)_assetFrame.Invoke(null, new object[] { asset, horizontalFrames, verticalFrames, horizontalFrame, verticalFrame, 0, 0 });
            }
        }
}
