using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Alacrity.App;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace AlacrityTerraria
{
    public static class PluginUiRuntime
    {
        private static PluginManagerRuntime _runtime;
        private static PluginManagementMenu _menu;
        private static PluginNotificationCenter _notifications;
        private static PluginDependencyDiagnostics _diagnostics;
        private static readonly PluginManagerPresenter _presenter = new PluginManagerPresenter();
        private static readonly Color ResourcePackBackground = new Color(26, 40, 89) * 0.8f;
        private static readonly Color ResourcePackBorder = new Color(13, 20, 44) * 0.8f;
        private static readonly Color ResourcePackHoverBackground = new Color(46, 60, 119);
        private static readonly Color ResourcePackHoverBorder = new Color(20, 30, 56);
        private static MethodInfo _assetRequest;
        private static MethodInfo _assetFrame;
        private static PropertyInfo _assetValue;
        private static Texture2D _ingameBlankTexture;
        private static bool _pluginMenuOpen;
        private static PluginSelectionMenu _selectionMenu;
        private static PluginManagerRow[] _ingameEntries = Array.Empty<PluginManagerRow>();
        private static int _ingameSelectedEntry;
        private static int _ingameView;
        private static float _ingameScroll;

        public static void Open()
        {
            EnsurePluginManager();
            RefreshPluginCatalog();
            Main.menuMode = 888;
            _selectionMenu = new PluginSelectionMenu(_menu);
            Main.MenuUI.SetState(_selectionMenu);
            _pluginMenuOpen = true;
        }

        public static bool HandlePluginMenuInput()
        {
            if (!_pluginMenuOpen || Main.menuMode != 888 || !Main.keyState.IsKeyDown(Keys.Escape) || Main.oldKeyState.IsKeyDown(Keys.Escape))
                return true;

            if (_selectionMenu != null && Main.MenuUI.CurrentState is PluginDescriptionMenu)
                ReturnToPluginList();
            else
                Close();
            return false;
        }

        public static void OpenIngamePluginSettings()
        {
            EnsurePluginManager();
            RefreshPluginCatalog();
            _ingameEntries = _presenter.Present(_runtime, _diagnostics.ActiveWarnings).ToArray();
            _ingameSelectedEntry = 0;
            _ingameView = 0;
            _ingameScroll = 0f;
        }

        public static void DrawIngamePluginSettings(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null)
                return;

            // Terraria centers General's right-column controls at screen center + 167.5.
            // Anchor this immediate-mode panel to that same verified column center.
            var bounds = new Rectangle(Main.screenWidth / 2 + 10, Main.screenHeight / 2 - 184, 315, 418);
            if (_ingameEntries.Length == 0)
            {
                Utils.DrawBorderString(spriteBatch, "No plugins installed.", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.75f, 0.5f, 0.5f, -1);
                return;
            }

            if (_ingameView == 1)
                DrawIngamePluginDescription(spriteBatch, bounds, _ingameEntries[_ingameSelectedEntry]);
            else
                DrawIngamePluginList(spriteBatch, bounds);
        }

        /// <summary>Renders active host notifications through Terraria's established gameplay UI SpriteBatch.</summary>
        public static void DrawNotifications(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || _notifications == null)
                return;

            int y = 96;
            foreach (var notification in _notifications.GetActive(DateTimeOffset.UtcNow))
            {
                Utils.DrawBorderString(spriteBatch, notification.Message, new Vector2(Main.screenWidth - 18, y), Color.LightGoldenrodYellow, 0.72f, 1f, 0f, -1);
                y += 24;
            }
        }

        private static void OpenDescription(PluginManagerRow plugin)
        {
            Main.MenuUI.SetState(new PluginDescriptionMenu(plugin));
        }

        private static void DrawIngamePluginList(SpriteBatch spriteBatch, Rectangle bounds)
        {
            const int rowHeight = 70;
            const int rowSpacing = 6;
            int contentTop = bounds.Y + 16;
            int contentHeight = _ingameEntries.Length * (rowHeight + rowSpacing) - rowSpacing;
            int visibleHeight = bounds.Height - 32;
            UpdateIngameScroll(bounds, contentHeight, visibleHeight);

            for (int index = 0; index < _ingameEntries.Length; index++)
            {
                int rowY = contentTop + index * (rowHeight + rowSpacing) - (int)_ingameScroll;
                const int rowWidth = 264;
                var row = new Rectangle(bounds.Center.X - rowWidth / 2, rowY, rowWidth, rowHeight);
                if (row.Bottom < contentTop || row.Top > bounds.Bottom - 16)
                    continue;

                DrawIngamePluginRow(spriteBatch, row, _ingameEntries[index]);
            }

            DrawIngameScrollbar(spriteBatch, bounds, contentHeight, visibleHeight);
        }

        private static void DrawIngamePluginRow(SpriteBatch spriteBatch, Rectangle row, PluginManagerRow plugin)
        {
            bool rowHovered = row.Contains(Main.mouseX, Main.mouseY);
            Utils.DrawInvBG(spriteBatch, row.X, row.Y, row.Width, row.Height, rowHovered ? ResourcePackHoverBackground : ResourcePackBackground);
            Utils.DrawBorderString(spriteBatch, plugin.Name, new Vector2(row.Center.X, row.Y + 9), Color.White, 0.8f, 0.5f, 0f, -1);
            if (!string.IsNullOrWhiteSpace(plugin.Warning))
                Utils.DrawBorderString(spriteBatch, plugin.Warning, new Vector2(row.Center.X, row.Y + 27), Color.Orange, 0.55f, 0.5f, 0f, -1);

            const int buttonHeight = 21;
            const int buttonGap = 6;
            int buttonWidth = (int)(((row.Width - buttonGap) / 2f) * 0.8f);
            int buttonsWidth = buttonWidth * 2 + buttonGap;
            int buttonY = string.IsNullOrWhiteSpace(plugin.Warning) ? row.Y + 40 : row.Y + 44;
            int buttonX = row.Center.X - buttonsWidth / 2;
            var description = new Rectangle(buttonX, buttonY, buttonWidth, buttonHeight);
            var settings = new Rectangle(buttonX + buttonWidth + buttonGap, buttonY, buttonWidth, buttonHeight);
            bool descriptionHovered = description.Contains(Main.mouseX, Main.mouseY);
            bool settingsHovered = settings.Contains(Main.mouseX, Main.mouseY);
            DrawIngameIconButton(spriteBatch, description, "Images/UI/CharCreation/CharInfo", descriptionHovered, "Plugin Description");
            DrawIngameIconButton(spriteBatch, settings, "Images/UI/Camera_1", settingsHovered, "Plugin Settings");

            if (!Main.mouseLeft || !Main.mouseLeftRelease)
                return;

            int index = Array.IndexOf(_ingameEntries, plugin);
            if (descriptionHovered)
            {
                _ingameSelectedEntry = index;
                _ingameView = 1;
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            else if (settingsHovered)
            {
                Main.instance.MouseText(plugin.CanConfigure ? "Plugin Settings" : "No plugin settings are available.");
            }
        }

        private static void DrawIngamePluginDescription(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
        {
            Utils.DrawBorderString(spriteBatch, plugin.Name, new Vector2(bounds.Center.X, bounds.Y + 16), Color.White, 0.9f, 0.5f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, "Version: " + plugin.Version, new Vector2(bounds.Center.X, bounds.Y + 50), Color.White, 0.7f, 0.5f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, "Description", new Vector2(bounds.X + 18, bounds.Y + 86), Color.White, 0.8f, 0f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, plugin.Description, new Vector2(bounds.X + 18, bounds.Y + 108), Color.White, 0.65f, 0f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, "Changelog", new Vector2(bounds.X + 18, bounds.Y + 176), Color.White, 0.8f, 0f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, plugin.Changelog, new Vector2(bounds.X + 18, bounds.Y + 198), Color.White, 0.65f, 0f, 0f, -1);

        }

        private static void DrawIngameActionButton(SpriteBatch spriteBatch, Rectangle bounds, string text, bool hovered)
        {
            Utils.DrawInvBG(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, hovered ? ResourcePackHoverBackground : ResourcePackBorder);
            Utils.DrawBorderString(spriteBatch, text, new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.6f, 0.5f, 0.5f, -1);
        }

        private static void DrawIngameIconButton(SpriteBatch spriteBatch, Rectangle bounds, string texturePath, bool hovered, string hoverText)
        {
            DrawResourcePackButtonBackground(spriteBatch, bounds, hovered);
            try
            {
                Texture2D texture = RequestTextureValue(texturePath);
                const int iconSize = 14;
                var destination = new Rectangle(bounds.Center.X - iconSize / 2, bounds.Center.Y - iconSize / 2, iconSize, iconSize);
                spriteBatch.Draw(texture, destination, Color.White);
            }
            catch
            {
                Utils.DrawBorderString(spriteBatch, "?", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.7f, 0.5f, 0.5f, -1);
            }

            if (hovered)
                Main.instance.MouseText(hoverText);
        }

        private static void DrawResourcePackButtonBackground(SpriteBatch spriteBatch, Rectangle bounds, bool hovered)
        {
            Texture2D panel = RequestTextureValue("Images/UI/CharCreation/PanelGrayscale");
            Color panelColor = Color.Lerp(Color.Black, Color.White, 0.8f) * 0.5f;
            Utils.DrawSplicedPanel(spriteBatch, panel, bounds.X, bounds.Y, bounds.Width, bounds.Height, 10, 10, 10, 10, panelColor);
            if (hovered)
            {
                Texture2D border = RequestTextureValue("Images/UI/CharCreation/CategoryPanelBorder");
                Utils.DrawSplicedPanel(spriteBatch, border, bounds.X, bounds.Y, bounds.Width, bounds.Height, 10, 10, 10, 10, Color.White);
            }
        }

        private static Texture2D RequestTextureValue(string path)
        {
            object assets = typeof(Main).GetField("Assets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if (_assetRequest == null)
            {
                _assetRequest = assets.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => {
                        ParameterInfo[] parameters = method.GetParameters();
                        return method.Name == "Request" && method.IsGenericMethodDefinition && parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType.IsEnum;
                    });
            }

            if (_assetRequest == null)
                throw new MissingMethodException(assets.GetType().FullName, "Request<T>(string, AssetRequestMode)");

            Type modeType = _assetRequest.GetParameters()[1].ParameterType;
            object asset = _assetRequest.MakeGenericMethod(typeof(Texture2D)).Invoke(assets, new object[] { path, Enum.ToObject(modeType, 1) });
            _assetValue = _assetValue ?? asset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return (Texture2D)_assetValue.GetValue(asset, null);
        }

        private static void UpdateIngameScroll(Rectangle bounds, int contentHeight, int visibleHeight)
        {
            if (bounds.Contains(Main.mouseX, Main.mouseY))
            {
                int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
                if (delta != 0)
                    _ingameScroll -= Math.Sign(delta) * 30f;
            }

            _ingameScroll = Math.Max(0f, Math.Min(_ingameScroll, Math.Max(0, contentHeight - visibleHeight)));
        }

        private static void DrawIngameScrollbar(SpriteBatch spriteBatch, Rectangle bounds, int contentHeight, int visibleHeight)
        {
            float maxScroll = Math.Max(0f, contentHeight - visibleHeight);
            if (maxScroll <= 0f)
                return;

            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture == null)
                return;

            int trackX = bounds.Right - 12;
            int trackY = bounds.Top;
            var track = new Rectangle(trackX, trackY, 4, bounds.Height);
            spriteBatch.Draw(_ingameBlankTexture, track, new Color(18, 12, 58, 180));

            int thumbHeight = Math.Max(28, (int)(bounds.Height * Math.Min(1f, visibleHeight / (float)contentHeight)));
            int thumbY = trackY + (int)((bounds.Height - thumbHeight) * (_ingameScroll / maxScroll));
            var thumb = new Rectangle(trackX - 1, thumbY, 6, thumbHeight);
            spriteBatch.Draw(_ingameBlankTexture, thumb, new Color(180, 170, 255, 220));
        }

        private static void EnsureIngameBlankTexture(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || (_ingameBlankTexture != null && !_ingameBlankTexture.IsDisposed))
                return;

            try
            {
                _ingameBlankTexture?.Dispose();
                _ingameBlankTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1);
                _ingameBlankTexture.SetData(new[] { Color.White });
            }
            catch
            {
                _ingameBlankTexture = null;
            }
        }

        private static void ReturnToPluginList()
        {
            if (_selectionMenu != null)
                Main.MenuUI.SetState(_selectionMenu);
        }

        private static void Close()
        {
            _pluginMenuOpen = false;
            _selectionMenu = null;
            Main.menuMode = 0;
            Main.MenuUI.SetState(null);
        }

        private static void EnsurePluginManager()
        {
            if (_menu != null)
                return;

            string root = AppDomain.CurrentDomain.BaseDirectory;
            _notifications = new PluginNotificationCenter();
            _diagnostics = new PluginDependencyDiagnostics();
            string patchDirectory = Path.Combine(root, "data", "patches");
            Directory.CreateDirectory(patchDirectory);
            var patchHost = PatchHost.CreateManaged(root, Path.Combine(patchDirectory, "journal.json"));
            var contexts = new PluginHostContextFactory(root, new PluginServiceHub(), new PluginExtensionHost(), new PluginCommandHost());
            var runtimeHost = new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), contexts);
            var activation = new PluginActivationCoordinator(patchHost, new PluginEnablePlanner(), new PluginEnableExecutor(_notifications), new PluginActivationGate(_diagnostics));
            _runtime = new PluginManagerRuntime(runtimeHost, new PluginPackageLifecycleRegistry(), activation);
            _menu = new PluginManagementMenu(_runtime);
        }

        private static void RefreshPluginCatalog()
        {
            _runtime.Discover(AppDomain.CurrentDomain.BaseDirectory);
            foreach (var record in _runtime.Registry.Records)
            {
                if (record.State != PluginPackageLifecycleState.Discovered || record.Manifest.EntryAssembly == null || record.Manifest.EntryType == null)
                    continue;

                try
                {
                    // Cryptographic release verification is intentionally staged. Until then,
                    // only packages present in the user's local package directory are loaded
                    // under an explicit local-development trust decision.
                    _runtime.LoadTrusted(record.Manifest.Id,
                        new PluginTrustVerificationResult(PluginTrustLevel.LocallyTrusted, "Locally installed package; cryptographic verification is not configured."),
                        new BridgePluginLogger(record.Manifest.Id),
                        new TerrariaMultiplayerSession());
                }
                catch (Exception exception)
                {
                    _runtime.Registry.MarkFaulted(record.Manifest.Id, exception.Message);
                }
            }
        }

        private sealed class PluginSelectionMenu : UIState
        {
            private readonly UIList _availableList = new UIList();
            private readonly UIList _enabledList = new UIList();
            private readonly UIText _availableTitle = new UIText("", 1f, false);
            private readonly UIText _enabledTitle = new UIText("", 1f, false);
            private UIGamepadHelper _gamepadHelper;
            private UIText _settingsHint;
            private DateTime _nextStatusRefreshUtc;

            public PluginSelectionMenu(PluginManagementMenu menu)
            {
                BuildPage();
            }

            private void BuildPage()
            {
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

                var title = new UITextPanel<string>("Plugins", 1f, true) {
                    HAlign = 0.5f,
                    VAlign = 0f,
                    Top = new StyleDimension(-44f, 0f),
                    BackgroundColor = new Color(73, 94, 171)
                };
                title.SetPadding(13f);
                root.Append(title);

                AddScrollbar(availableContainer, _availableList, 0f);
                AddScrollbar(enabledContainer, _enabledList, 1f);
                AddSeparator(panel);
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
                var back = new UITextPanel<string>("Back", 0.7f, true) {
                    Width = new StyleDimension(-8f, 0.5f),
                    Height = new StyleDimension(50f, 0f),
                    VAlign = 1f,
                    HAlign = 0f,
                    Top = new StyleDimension(-45f, 0f)
                };
                back.OnMouseOver += (evt, element) => FadedMouseOver((UIPanel)element);
                back.OnMouseOut += (evt, element) => FadedMouseOut((UIPanel)element);
                back.OnLeftClick += (evt, element) => Close();
                back.SetSnapPoint("GoBack", 0, null, null);
                root.Append(back);

                var folder = new UITextPanel<string>("Open Folder", 0.7f, true) {
                    Width = new StyleDimension(-8f, 0.5f),
                    Height = new StyleDimension(50f, 0f),
                    VAlign = 1f,
                    HAlign = 1f,
                    Top = new StyleDimension(-45f, 0f)
                };
                folder.OnMouseOver += (evt, element) => FadedMouseOver((UIPanel)element);
                folder.OnMouseOut += (evt, element) => FadedMouseOut((UIPanel)element);
                folder.OnLeftClick += (evt, element) => OpenPluginsFolder();
                folder.SetSnapPoint("OpenFolder", 0, null, null);
                root.Append(folder);

                _settingsHint = new UIText("", 0.7f, false) {
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Top = new StyleDimension(-104f, 0f)
                };
                root.Append(_settingsHint);
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

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                RefreshRuntimeStatusHint(false);
                SetupGamepadPoints(spriteBatch);
            }

            private void SetupGamepadPoints(SpriteBatch spriteBatch)
            {
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
                int startId = 3000;
                int nextId = startId;
                var allPoints = GetSnapPoints();
                var availablePoints = _availableList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, availablePoints, _availableList);
                var enabledPoints = _enabledList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, enabledPoints, _enabledList);

                var availableDescription = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "DescriptionOff");
                var availableSettings = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "SettingsOff");
                var availableToggle = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "ToggleToOn");
                var enabledDescription = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "DescriptionOn");
                var enabledSettings = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "SettingsOn");
                var enabledToggle = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "ToggleToOff");
                UILinkPoint back = null;
                UILinkPoint folder = null;
                foreach (SnapPoint point in allPoints)
                {
                    if (point.Name == "GoBack")
                        back = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "OpenFolder")
                        folder = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                }

                _gamepadHelper.LinkVerticalStrips(availableDescription, availableSettings, 0);
                _gamepadHelper.LinkVerticalStrips(availableSettings, availableToggle, 0);
                _gamepadHelper.LinkVerticalStrips(availableToggle, enabledDescription, 0);
                _gamepadHelper.LinkVerticalStrips(enabledDescription, enabledSettings, 0);
                _gamepadHelper.LinkVerticalStrips(enabledSettings, enabledToggle, 0);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableToggle, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableSettings, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableDescription, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledToggle, folder);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledSettings, folder);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledDescription, folder);
                _gamepadHelper.PairLeftRight(back, folder);
                _gamepadHelper.MoveToVisuallyClosestPoint(startId, nextId);
            }

            private void RefreshLists()
            {
                _availableList.Clear();
                _enabledList.Clear();
                int order = 0;
                foreach (PluginManagerRow plugin in _presenter.Present(_runtime, _diagnostics.ActiveWarnings))
                {
                    UIElement row = CreatePluginRow(plugin, order++);
                    if (plugin.IsEnabled)
                        _enabledList.Add(row);
                    else
                        _availableList.Add(row);
                }

                _availableTitle.SetText("Available Plugins (" + _availableList.Count + ")");
                _enabledTitle.SetText("Enabled Plugins (" + _enabledList.Count + ")");
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

                var author = new UIText("Alacrity", 0.7f, false) {
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

                var description = CreateDescriptionButton(plugin);
                description.Width = new StyleDimension(0f, 1f / 3f);
                description.Height = StyleDimension.Fill;
                description.SetSnapPoint(plugin.IsEnabled ? "DescriptionOn" : "DescriptionOff", order, null, null);
                description.OnLeftClick += (evt, element) => OpenDescription(plugin);
                content.Append(description);

                var settings = CreateSettingsButton(plugin);
                settings.Left = StyleDimension.FromPercent(1f / 3f);
                settings.Width = new StyleDimension(0f, 1f / 3f);
                settings.Height = StyleDimension.Fill;
                settings.SetSnapPoint(plugin.IsEnabled ? "SettingsOn" : "SettingsOff", order, null, null);
                settings.OnLeftClick += (evt, element) => _settingsHint.SetText("No settings are exposed by " + plugin.Name + ".");
                content.Append(settings);

                var toggle = CreateToggleButton(plugin);
                toggle.Left = StyleDimension.FromPercent(2f / 3f);
                toggle.Width = new StyleDimension(0f, 1f / 3f);
                toggle.Height = StyleDimension.Fill;
                toggle.VAlign = 0f;
                toggle.SetSnapPoint(plugin.IsEnabled ? "ToggleToOff" : "ToggleToOn", order, null, null);
                if (plugin.CanToggle)
                {
                    toggle.OnLeftClick += (evt, element) => {
                        try
                        {
                            if (plugin.IsEnabled)
                                _runtime.Disable(plugin.Id);
                            else
                                _runtime.Enable(plugin.Id);
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

            private void RefreshRuntimeStatusHint(bool force)
            {
                var now = DateTime.UtcNow;
                if (!force && now < _nextStatusRefreshUtc)
                    return;

                _nextStatusRefreshUtc = now.AddMilliseconds(250);
                var active = _notifications.GetActive(new DateTimeOffset(now));
                string text;
                if (active.Count > 0)
                    text = active[active.Count - 1].Message;
                else
                {
                    var warning = _diagnostics.ActiveWarnings.FirstOrDefault();
                    text = warning == null ? string.Empty : warning.Plugin + ": " + warning.Reason;
                }
                _settingsHint.SetText(text);
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
                description.OnUpdate += (element) => DisplayMouseTextIfHovered(element, "Plugin Description");
                return description;
            }

            private static UIResourcePackInfoButton<string> CreateSettingsButton(PluginManagerRow plugin)
            {
                var settings = new UIResourcePackInfoButton<string>("", 0.8f, false);
                settings.SetPadding(0f);
                AppendPluginSettingsIcon(settings);
                settings.OnUpdate += (element) => DisplayMouseTextIfHovered(element, "Plugin Settings");
                return settings;
            }

            private static void DisplayMouseTextIfHovered(UIElement element, string text)
            {
                if (element.IsMouseHovering)
                    Main.instance.MouseText(text);
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

            private static object RequestTexture(string path)
            {
                object assets = typeof(Main).GetField("Assets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
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

        private sealed class PluginDescriptionMenu : UIState
        {
            private readonly PluginManagerRow _plugin;

            public PluginDescriptionMenu(PluginManagerRow plugin)
            {
                _plugin = plugin;
            }

            public override void OnInitialize()
            {
                var panel = new UIPanel {
                    Width = new StyleDimension(-260f, 1f),
                    MaxWidth = new StyleDimension(900f, 0f),
                    Height = new StyleDimension(-180f, 1f),
                    MaxHeight = new StyleDimension(560f, 0f),
                    HAlign = 0.5f,
                    VAlign = 0.5f,
                    BackgroundColor = ResourcePackBackground,
                    BorderColor = ResourcePackBorder
                };
                panel.SetPadding(18f);
                Append(panel);

                var title = new UIText(_plugin.Name, 1.1f, true) {
                    HAlign = 0.5f,
                    Top = new StyleDimension(10f, 0f)
                };
                panel.Append(title);

                var author = new UIText("Author: " + _plugin.Author, 0.8f, false) {
                    Left = new StyleDimension(14f, 0f),
                    Top = new StyleDimension(52f, 0f)
                };
                panel.Append(author);

                var version = new UIText("Version: " + _plugin.Version, 0.8f, false) {
                    HAlign = 1f,
                    Left = new StyleDimension(-14f, 0f),
                    Top = new StyleDimension(52f, 0f)
                };
                panel.Append(version);

                AppendSection(panel, "Description", _plugin.Description, 92f, 106f, 92f);
                AppendSection(panel, "Changelog", _plugin.Changelog, 214f, 228f, 112f);

                var back = new UITextPanel<string>("Back", 0.7f, true) {
                    Width = new StyleDimension(-8f, 0.5f),
                    Height = new StyleDimension(50f, 0f),
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Top = new StyleDimension(-20f, 0f)
                };
                back.OnMouseOver += (evt, element) => FadedMouseOver((UIPanel)element);
                back.OnMouseOut += (evt, element) => FadedMouseOut((UIPanel)element);
                back.OnLeftClick += (evt, element) => ReturnToPluginList();
                panel.Append(back);
            }

            private static void AppendSection(UIElement panel, string heading, string text, float headingTop, float textTop, float height)
            {
                var title = new UIText(heading, 0.85f, true) {
                    Left = new StyleDimension(14f, 0f),
                    Top = new StyleDimension(headingTop, 0f)
                };
                panel.Append(title);

                var content = new UIText(text, 0.75f, false) {
                    Left = new StyleDimension(14f, 0f),
                    Top = new StyleDimension(textTop, 0f),
                    Width = new StyleDimension(-28f, 1f),
                    Height = new StyleDimension(height, 0f),
                    IsWrapped = true
                };
                panel.Append(content);
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
        }

        private sealed class BridgePluginLogger : IPluginLogger
        {
            private readonly PluginId plugin;
            public BridgePluginLogger(PluginId plugin) { this.plugin = plugin; }
            public void Debug(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Info(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Warn(string message) { System.Diagnostics.Trace.TraceWarning("[Alacrity:" + plugin + "] " + message); }
            public void Error(string message, Exception exception = null) { System.Diagnostics.Trace.TraceError("[Alacrity:" + plugin + "] " + message + (exception == null ? string.Empty : " " + exception)); }
        }

        private sealed class TerrariaMultiplayerSession : IMultiplayerSession
        {
            public bool IsConnected { get { return Main.netMode == 1; } }
            public bool IsVanillaCompatibleMode { get { return true; } }
            public bool IsAlacrityAwareServer { get { return false; } }
            public ServerIdentity Server { get { return null; } }
            public ServerPluginPolicySnapshot ActivePolicy { get { return null; } }
        }
    }
}
