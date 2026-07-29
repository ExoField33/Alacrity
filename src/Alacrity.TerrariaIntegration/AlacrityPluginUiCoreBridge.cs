using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.App;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Chat;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.UI;
using Terraria.UI.Chat;
using Terraria.UI.Gamepad;
using Terraria.Utilities;

namespace AlacrityTerraria
{
    public static class PluginUiRuntime
    {
        private static PluginManagerRuntime _runtime;
        private static PluginManagementMenu _menu;
        private static PluginNotificationCenter _notifications;
        private static PluginDependencyDiagnostics _diagnostics;
        private static PluginExtensionHost _extensions;
        private static PluginServiceHub _serviceHub;
        private static PluginChatHost _chat;
        private static PluginUserInteractionHost _userInteraction;
        private static IPluginUserInteractionService _betterChatUserInteraction;
        private static readonly PluginManagerPresenter _presenter = new PluginManagerPresenter();
        private static readonly Color ResourcePackBackground = new Color(26, 40, 89) * 0.8f;
        private static readonly Color ResourcePackBorder = new Color(13, 20, 44) * 0.8f;
        private static readonly Color ResourcePackHoverBackground = new Color(46, 60, 119);
        private static readonly Color ResourcePackHoverBorder = new Color(20, 30, 56);
        private static MethodInfo _assetRequest;
        private static MethodInfo _assetFrame;
        private static PropertyInfo _assetValue;
        private static FieldInfo _mainAssetsField;
        private static bool _pingLookupAttempted;
        private static PropertyInfo _currentPingProperty;
        private static DateTime _nextPingSampleUtc;
        private static int? _cachedPing;
        private static readonly HashSet<string> ReportedOptionalUiFailures = new HashSet<string>(StringComparer.Ordinal);
        private static Texture2D _ingameBlankTexture;
        private static bool _pluginMenuOpen;
        private static PluginSelectionMenu _selectionMenu;
        private static PluginManagerRow[] _ingameEntries = Array.Empty<PluginManagerRow>();
        private static int _ingameSelectedEntry;
        private static int _ingameView;
        private static float _ingameScroll;
        private static float _ingameDescriptionScroll;
        private static string _ingameHoveredSettingId;
        private static bool _enabledStateRestored;
        private static readonly object RuntimeGate = new object();
        private static readonly PluginId BetterChatPluginId = new PluginId("alacrity.better-chat");
        private static readonly PluginId PlayerListPluginId = new PluginId("alacrity.player-list");
        private static bool _runtimeBootstrapped;
        private static bool _runtimeShuttingDown;
        private static readonly Dictionary<string, bool> KeybindDownState = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<PluginId, PendingPluginOperation> PendingPluginOperations = new Dictionary<PluginId, PendingPluginOperation>();
        private static long _keybindRegistryVersion = -1;
        private static readonly ConditionalWeakTable<UIManageControls, KeybindControlsState> KeybindControlsStates = new ConditionalWeakTable<UIManageControls, KeybindControlsState>();
        private static FieldInfo _controlsListField;
        private static FieldInfo _controlsKeyboardField;
        private static FieldInfo _controlsGameplayField;
        private static readonly object KeybindPersistenceGate = new object();
        private static readonly Dictionary<string, List<string>> PersistedPluginKeybinds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private static bool _pluginKeybindsLoaded;
        private static int _pluginKeybindSaveQueued;
        private static bool _pluginKeybindsDirty;

        /// <summary>Creates and starts the package runtime once during normal Terraria startup.</summary>
        public static void BootstrapPluginRuntime()
        {
            if (Volatile.Read(ref _runtimeBootstrapped) || Volatile.Read(ref _runtimeShuttingDown))
                return;
            lock (RuntimeGate)
            {
                if (Volatile.Read(ref _runtimeBootstrapped) || Volatile.Read(ref _runtimeShuttingDown))
                    return;
                EnsurePluginManager();
                RefreshPluginCatalog();
                Volatile.Write(ref _runtimeBootstrapped, true);
            }
        }

        /// <summary>Best-effort process-exit cleanup. Individual plugin failures never block Terraria shutdown.</summary>
        public static void ShutdownPluginRuntime()
        {
            if (Volatile.Read(ref _runtimeShuttingDown))
                return;
            lock (RuntimeGate)
            {
                if (Volatile.Read(ref _runtimeShuttingDown))
                    return;
                Volatile.Write(ref _runtimeShuttingDown, true);
                if (_runtime == null)
                    return;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                foreach (var record in GetShutdownOrder())
                {
                    try
                    {
                        if (record.Controller != null && record.Controller.UsesAsyncLifecycle)
                            record.Controller.DisposeAsync(cancellation.Token).GetAwaiter().GetResult();
                        else
                            record.Controller?.Dispose();
                    }
                    catch (Exception exception) { ReportOptionalUiFailure("Plugin shutdown: " + record.Manifest.Id, exception); }
                }
            }
        }

        /// <summary>Returns whether an enabled plugin owns a chat editor. The injected hook calls this only while player chat is focused.</summary>
        public static bool IsBetterChatActive()
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasInputEditor(BetterChatPluginId);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat activation", exception);
                return false;
            }
        }

        /// <summary>Processes player-chat input through the enabled scoped chat editor registrations.</summary>
        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasInputEditor(BetterChatPluginId) ? BetterChatRuntime.Process(_chat, GetBetterChatUserInteraction(), text, allowMultiLine) : text;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat input", exception);
                return text;
            }
        }

        /// <summary>Creates draw-only chat markup. It never modifies Main.chatText or outgoing packet text.</summary>
        public static string FormatPlayerChatText(string text)
        {
            try { return BetterChatRuntime.FormatForDraw(IsBetterChatActive(), text); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat draw text", exception); return text; }
        }

        /// <summary>Decorates parsed normal chat snippets outside the draw loop.</summary>
        public static object DecorateChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasMessageDecorators ? BetterChatRuntime.Decorate(_chat, snippets, baseColor, originalMessage) : snippets;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat message decoration", exception);
                return snippets;
            }
        }

        /// <summary>Filters network chat before Terraria creates overhead or scrolling-chat entries.</summary>
        public static bool ShouldDisplayNetworkChatMessage(byte messageAuthor)
        {
            try
            {
                EnsureChatRuntime();
                if (_chat == null || !_chat.HasMessageFilters) return true;
                return _chat.ShouldDisplay(messageAuthor == byte.MaxValue ? ChatMessageOrigin.Server : ChatMessageOrigin.Player);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat network visibility", exception);
                return true;
            }
        }

        /// <summary>Filters client-originated system messages without affecting network receive behavior.</summary>
        public static bool ShouldDisplayLocalChatMessage()
        {
            try
            {
                EnsureChatRuntime();
                return _chat == null || !_chat.HasMessageFilters || _chat.ShouldDisplay(ChatMessageOrigin.LocalSystem);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat local visibility", exception);
                return true;
            }
        }

        /// <summary>Shows bounded vanilla-style hover feedback and handles copy-on-right-click.</summary>
        public static void HandleChatSnippetHover(object snippet)
        {
            try { if (IsBetterChatActive()) BetterChatRuntime.Hover(snippet, GetBetterChatUserInteraction()); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat hover", exception); }
        }

        /// <summary>Activates only validated http or https links registered by an enabled plugin.</summary>
        public static bool HandleChatSnippetClick(object snippet)
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasLinkHandlers && BetterChatRuntime.Click(_chat, snippet);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat link activation", exception);
                return false;
            }
        }

        /// <summary>Applies the current hover highlight without mutating the original snippet color.</summary>
        public static Color GetChatSnippetVisibleColor(object snippet, Color color)
        {
            try { return BetterChatRuntime.VisibleColor(snippet, color); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat hover color", exception); return color; }
        }

        /// <summary>Transfers parse-time line ownership when Terraria clones a snippet during layout.</summary>
        public static void CopyChatSnippetContext(object source, object copy)
        {
            try { BetterChatRuntime.CopyContext(source, copy); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat snippet copy", exception); }
        }

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
            _ingameEntries = _presenter.Present(_runtime, _diagnostics.ActiveWarnings)
                .Where(entry => entry.IsEnabled)
                .ToArray();
            _ingameSelectedEntry = 0;
            _ingameView = 0;
            _ingameScroll = 0f;
            _ingameDescriptionScroll = 0f;
            _ingameHoveredSettingId = null;
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
            else if (_ingameView == 2)
                DrawIngamePluginSettingsPage(spriteBatch, bounds, _ingameEntries[_ingameSelectedEntry]);
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

        /// <summary>Draws the Player List only while its owning plugin has published an active presentation service.</summary>
        public static void DrawPlayerList(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || _serviceHub == null ||
                !_serviceHub.TryGetHostService<IPlayerListService>(PlayerListPluginId, out var playerList, out var publisher) ||
                !CanRenderPlayerList(publisher) || !TryCreatePlayerListSnapshot(playerList, out var snapshot))
            {
                PlayerListRuntime.Reset();
                return;
            }

            try
            {
                PlayerListRuntime.Draw(spriteBatch, snapshot);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Player List draw", exception);
            }
        }

        private static bool CanRenderPlayerList(PluginManifest publisher)
        {
            const PluginCapability capabilities = PluginCapability.UserInterface | PluginCapability.GameStateRead | PluginCapability.MultiplayerObservation;
            const PluginPermission permissions = PluginPermission.DrawUserInterface | PluginPermission.ReadGameState | PluginPermission.ObserveMultiplayer;
            return publisher != null && (publisher.Capabilities & capabilities) == capabilities && (publisher.Permissions & permissions) == permissions;
        }

        private static bool TryCreatePlayerListSnapshot(IPlayerListService service, out PlayerListRuntime.PlayerListRenderSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                int playersPerColumn = Math.Max(8, Math.Min(20, service.PlayersPerColumn));
                int rowWidth = Math.Max(180, Math.Min(420, service.RowWidth));
                float textScale = service.TextScale;
                if (float.IsNaN(textScale) || float.IsInfinity(textScale))
                    return false;
                textScale = Math.Max(0.8f, Math.Min(1.6f, textScale));
                PlayerListSortMode sortMode = Enum.IsDefined(typeof(PlayerListSortMode), service.SortMode) ? service.SortMode : PlayerListSortMode.Alphabetical;
                snapshot = new PlayerListRuntime.PlayerListRenderSnapshot(service.IsVisible, playersPerColumn, rowWidth, textScale, service.ShowPlayerHeads, service.ShowPing, service.HideBots, sortMode, service.CycleSortMode, service.ToggleBotFiltering);
                return true;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Player List service snapshot", exception);
                return false;
            }
        }

        /// <summary>
        /// Appends verified plugin bindings to Terraria's native controls list. The controls adapter
        /// is deliberately optional: a changed UI signature leaves vanilla controls untouched.
        /// </summary>
        public static void AppendPluginKeybindControls(UIManageControls controls)
        {
            if (!Volatile.Read(ref _runtimeBootstrapped) || _extensions == null || controls == null)
                return;

            try
            {
                if (!TryGetControlsList(controls, out var list))
                    return;
                PluginKeybindRegistrySnapshot snapshot = _extensions.GetKeybindSnapshot();
                var state = KeybindControlsStates.GetOrCreateValue(controls);
                if (state.Version == snapshot.Version)
                    return;

                RemovePluginKeybindGroups(list);
                state.Version = snapshot.Version;
                if (snapshot.Registrations.Count == 0)
                    return;

                var mode = GetControlsInputMode(controls);
                // Runtime dispatch currently uses Terraria's gameplay keyboard profile only.
                // Do not show rows in UI modes that cannot activate them.
                if (mode != InputMode.Keyboard)
                    return;

                int groupOrder = 10000;
                int rowOrder = 20000;
                foreach (var group in snapshot.Registrations.GroupBy(keybind => keybind.Owner.Value + "\u001f" + keybind.Heading, StringComparer.Ordinal))
                    list.Add(CreatePluginKeybindGroup(groupOrder++, ref rowOrder, group.First().Heading, group.ToArray(), mode));
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin controls-menu adapter", exception);
            }
        }

        private static bool TryGetControlsList(UIManageControls controls, out UIList list)
        {
            list = null;
            _controlsListField ??= typeof(UIManageControls).GetField("_uilist", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_controlsListField == null || _controlsListField.FieldType != typeof(UIList))
                throw new MissingFieldException(typeof(UIManageControls).FullName, "_uilist");
            list = _controlsListField.GetValue(controls) as UIList;
            return list != null;
        }

        private static InputMode GetControlsInputMode(UIManageControls controls)
        {
            _controlsKeyboardField ??= typeof(UIManageControls).GetField("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);
            _controlsGameplayField ??= typeof(UIManageControls).GetField("OnGameplay", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_controlsKeyboardField?.FieldType != typeof(bool) || _controlsGameplayField?.FieldType != typeof(bool))
                throw new MissingFieldException(typeof(UIManageControls).FullName, "OnKeyboard/OnGameplay");

            bool keyboard = (bool)_controlsKeyboardField.GetValue(controls);
            bool gameplay = (bool)_controlsGameplayField.GetValue(controls);
            if (keyboard)
                return gameplay ? InputMode.Keyboard : InputMode.KeyboardUI;
            return gameplay ? InputMode.XBoxGamepad : InputMode.XBoxGamepadUI;
        }

        private static void RemovePluginKeybindGroups(UIList list)
        {
            foreach (var existing in list.Where(element => element is PluginKeybindControlGroup).ToArray())
                list.Remove(existing);
        }

        private static UIElement CreatePluginKeybindGroup(int groupOrder, ref int rowOrder, string heading, IReadOnlyList<PluginKeybindRegistration> keybinds, InputMode mode)
        {
            var group = new PluginKeybindControlGroup(groupOrder) { HAlign = 0.5f, Width = StyleDimension.Fill, Height = new StyleDimension(2000f, 0f) };
            var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-16f, 1f), VAlign = 1f, BackgroundColor = Color.Lerp(new Color(33, 43, 79) * 0.8f, Color.MediumPurple, 0.18f) };
            group.Append(panel);
            var rows = new UIList { OverflowHidden = false, Width = StyleDimension.Fill, Height = new StyleDimension(-8f, 1f), VAlign = 1f, ListPadding = 5f };
            panel.Append(rows);
            foreach (var keybind in keybinds)
            {
                EnsureInputBinding(keybind, mode);
                int currentRowOrder = rowOrder++;
                var row = new UISortableElement(currentRowOrder) { Width = StyleDimension.Fill, Height = new StyleDimension(30f, 0f), HAlign = 0.5f };
                var item = new PluginKeybindingListItem(keybind, mode, panel.BackgroundColor) { Width = StyleDimension.Fill, Height = StyleDimension.Fill };
                item.SetSnapPoint("Wide", currentRowOrder);
                row.Append(item);
                rows.Add(row);
            }

            panel.BackgroundColor = panel.BackgroundColor.MultiplyRGBA(new Color(111, 111, 111));
            group.Append(new UITextPanel<string>(heading, 0.7f) { VAlign = 0f, HAlign = 0.5f });
            group.Recalculate();
            group.Height = new StyleDimension(rows.GetTotalHeight() + 46f, 0f);
            return group;
        }

        private static void EnsureInputBinding(PluginKeybindRegistration keybind, InputMode mode)
        {
            var configuration = PlayerInput.CurrentProfile.InputModes[mode];
            if (!configuration.KeyStatus.ContainsKey(keybind.HostId))
            {
                EnsurePluginKeybindsLoaded();
                List<string> bindings;
                lock (KeybindPersistenceGate)
                {
                    if (!PersistedPluginKeybinds.TryGetValue(GetPersistedKeybindKey(keybind, mode), out var saved))
                    {
                        // Import the pre-profile persistence format once, then write it back with the
                        // selected Terraria profile encoded into the key on the next real change.
                        PersistedPluginKeybinds.TryGetValue(GetLegacyPersistedKeybindKey(keybind, mode), out saved);
                    }
                    bindings = saved == null ? (mode == InputMode.Keyboard ? new List<string> { keybind.Descriptor.DefaultBinding } : new List<string>()) : new List<string>(saved);
                }
                configuration.KeyStatus.Add(keybind.HostId, bindings);
            }
        }

        private static void ObservePluginKeybindBindings(PluginKeybindRegistration keybind, InputMode mode, IReadOnlyList<string> bindings)
        {
            EnsurePluginKeybindsLoaded();
            string key = GetPersistedKeybindKey(keybind, mode);
            lock (KeybindPersistenceGate)
            {
                if (PersistedPluginKeybinds.TryGetValue(key, out var current) && current.SequenceEqual(bindings, StringComparer.Ordinal))
                    return;
                PersistedPluginKeybinds[key] = bindings.ToList();
                _pluginKeybindsDirty = true;
            }

            QueuePluginKeybindPersistence();
        }

        private static string GetPersistedKeybindKey(PluginKeybindRegistration keybind, InputMode mode)
        {
            string profile = PlayerInput.CurrentProfile == null || string.IsNullOrWhiteSpace(PlayerInput.CurrentProfile.Name)
                ? "default"
                : PlayerInput.CurrentProfile.Name;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(profile)) + ":" + ((int)mode).ToString() + ":" + keybind.HostId;
        }

        private static string GetLegacyPersistedKeybindKey(PluginKeybindRegistration keybind, InputMode mode) => ((int)mode).ToString() + ":" + keybind.HostId;

        private static string PluginKeybindsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugin-keybinds.dat");

        private static void EnsurePluginKeybindsLoaded()
        {
            lock (KeybindPersistenceGate)
            {
                if (_pluginKeybindsLoaded)
                    return;
                _pluginKeybindsLoaded = true;
                try
                {
                    if (!File.Exists(PluginKeybindsPath))
                        return;
                    foreach (string line in File.ReadAllLines(PluginKeybindsPath))
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length != 2)
                            continue;
                        string key = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                        var bindings = new List<string>();
                        if (!string.IsNullOrEmpty(parts[1]))
                            foreach (string encoded in parts[1].Split(','))
                                bindings.Add(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                        PersistedPluginKeybinds[key] = bindings;
                    }
                }
                catch (Exception exception)
                {
                    ReportOptionalUiFailure("Plugin keybind persistence load", exception);
                    PersistedPluginKeybinds.Clear();
                }
            }
        }

        private static void QueuePluginKeybindPersistence()
        {
            if (Interlocked.Exchange(ref _pluginKeybindSaveQueued, 1) != 0)
                return;
            ThreadPool.QueueUserWorkItem(_ => SavePluginKeybinds());
        }

        private static void SavePluginKeybinds()
        {
            Dictionary<string, List<string>> snapshot;
            lock (KeybindPersistenceGate)
            {
                if (!_pluginKeybindsDirty)
                {
                    Interlocked.Exchange(ref _pluginKeybindSaveQueued, 0);
                    return;
                }
                snapshot = new Dictionary<string, List<string>>(PersistedPluginKeybinds.Count, StringComparer.Ordinal);
                foreach (var pair in PersistedPluginKeybinds)
                    snapshot.Add(pair.Key, new List<string>(pair.Value));
                _pluginKeybindsDirty = false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PluginKeybindsPath));
                var lines = snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)) + "|" + string.Join(",", pair.Value.Select(binding => Convert.ToBase64String(Encoding.UTF8.GetBytes(binding))))).ToArray();
                string temporaryPath = PluginKeybindsPath + ".tmp";
                File.WriteAllLines(temporaryPath, lines);
                if (File.Exists(PluginKeybindsPath))
                    File.Replace(temporaryPath, PluginKeybindsPath, null);
                else
                    File.Move(temporaryPath, PluginKeybindsPath);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin keybind persistence save", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _pluginKeybindSaveQueued, 0);
                lock (KeybindPersistenceGate)
                {
                    if (_pluginKeybindsDirty)
                        QueuePluginKeybindPersistence();
                }
            }
        }

        /// <summary>
        /// Polls host-owned plugin bindings at the established gameplay UI boundary. The native
        /// input profile remains the source of persisted bindings; no plugin receives raw input.
        /// </summary>
        public static void UpdatePluginKeybinds()
        {
            if (!Volatile.Read(ref _runtimeBootstrapped) || Volatile.Read(ref _runtimeShuttingDown) || _extensions == null)
                return;
            if (Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.blockInput)
                return;

            try
            {
                PluginKeybindRegistrySnapshot snapshot = _extensions.GetKeybindSnapshot();
                var keybinds = snapshot.Registrations;
                if (keybinds.Count == 0)
                {
                    KeybindDownState.Clear();
                    return;
                }

                if (_keybindRegistryVersion != snapshot.Version)
                    RemoveStaleKeybindState(keybinds, snapshot.Version);

                var keyboardState = Keyboard.GetState();
                foreach (var keybind in keybinds)
                {
                    bool isDown = IsKeybindDown(keybind, keyboardState);
                    bool wasDown = KeybindDownState.TryGetValue(keybind.HostId, out var previous) && previous;
                    KeybindDownState[keybind.HostId] = isDown;
                    bool changed = isDown != wasDown;
                    bool invoked;
                    Exception failure;
                    if (keybind.Descriptor.Activation == PluginKeybindActivation.Hold)
                    {
                        if (!changed)
                            continue;
                        invoked = _extensions.TrySetKeybindState(keybind.HostId, isDown, out failure);
                    }
                    else
                    {
                        if (!isDown || wasDown)
                            continue;
                        invoked = _extensions.TryInvokeKeybind(keybind.HostId, out failure);
                    }

                    if (!invoked && failure != null)
                    {
                        _notifications?.Publish("Plugin keybind failed: " + keybind.Heading, TimeSpan.FromSeconds(4));
                        ReportOptionalUiFailure("Plugin keybind " + keybind.HostId, failure);
                    }
                }

            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin keybind dispatch", exception);
            }
        }

        private static void RemoveStaleKeybindState(IReadOnlyList<PluginKeybindRegistration> keybinds, long version)
        {
            var active = new HashSet<string>(keybinds.Select(keybind => keybind.HostId), StringComparer.Ordinal);
            foreach (var stale in KeybindDownState.Keys.Where(key => !active.Contains(key)).ToArray())
                KeybindDownState.Remove(stale);
            PrunePersistedKeybinds(active);
            _keybindRegistryVersion = version;
        }

        private static void PrunePersistedKeybinds(HashSet<string> active)
        {
            EnsurePluginKeybindsLoaded();
            bool changed = false;
            lock (KeybindPersistenceGate)
            {
                foreach (string key in PersistedPluginKeybinds.Keys.Where(key => !active.Any(hostId => key.EndsWith(":" + hostId, StringComparison.Ordinal))).ToArray())
                {
                    PersistedPluginKeybinds.Remove(key);
                    changed = true;
                }
                _pluginKeybindsDirty |= changed;
            }
            if (changed)
                QueuePluginKeybindPersistence();
        }

        private static bool IsKeybindDown(PluginKeybindRegistration keybind, KeyboardState keyboardState)
        {
            var configuration = PlayerInput.CurrentProfile?.InputModes[Terraria.GameInput.InputMode.Keyboard];
            if (configuration == null)
                return false;

            EnsureInputBinding(keybind, InputMode.Keyboard);
            var bindings = configuration.KeyStatus[keybind.HostId];

            foreach (var binding in bindings)
            {
                if (Enum.TryParse(binding, true, out Keys key) && keyboardState.IsKeyDown(key))
                    return true;
            }

            return false;
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
                _ingameDescriptionScroll = 0f;
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            else if (settingsHovered)
            {
                if (!HasSettings(plugin.Id))
                    Main.instance.MouseText("No plugin settings are available.");
                else
                {
                    _ingameSelectedEntry = index;
                    _ingameView = 2;
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                }
            }
        }

        private static void DrawIngamePluginDescription(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
        {
            Utils.DrawBorderString(spriteBatch, plugin.Name, new Vector2(bounds.Center.X, bounds.Y + 16), Color.White, 0.9f, 0.5f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, "Version: " + plugin.Version, new Vector2(bounds.Center.X, bounds.Y + 50), Color.White, 0.7f, 0.5f, 0f, -1);
            string body = "Description\n" + (string.IsNullOrWhiteSpace(plugin.Description) ? "No information provided." : plugin.Description) + "\n\nChangelog\n" + (string.IsNullOrWhiteSpace(plugin.Changelog) ? "No information provided." : plugin.Changelog);
            string[] lines = WrapPluginText(body, 44);
            const int top = 82;
            const int lineHeight = 17;
            int visibleHeight = bounds.Height - top - 18;
            int contentHeight = lines.Length * lineHeight;
            UpdateIngameDescriptionScroll(bounds, contentHeight, visibleHeight);
            int firstLine = (int)(_ingameDescriptionScroll / lineHeight);
            int lastLine = Math.Min(lines.Length, firstLine + visibleHeight / lineHeight + 2);
            for (int index = firstLine; index < lastLine; index++)
            {
                bool heading = lines[index] == "Description" || lines[index] == "Changelog";
                Utils.DrawBorderString(spriteBatch, lines[index], new Vector2(bounds.X + 18, bounds.Y + top + index * lineHeight - _ingameDescriptionScroll), heading ? Color.White : Color.White, heading ? 0.8f : 0.65f, 0f, 0f, -1);
            }
            DrawIngameDescriptionScrollbar(spriteBatch, bounds, contentHeight, visibleHeight);
        }

        private static string[] WrapPluginText(string text, int maximumCharacters)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length > maximumCharacters)
                    {
                        if (current.Length != 0) { lines.Add(current); current = string.Empty; }
                        for (int offset = 0; offset < word.Length; offset += maximumCharacters)
                            lines.Add(word.Substring(offset, Math.Min(maximumCharacters, word.Length - offset)));
                        continue;
                    }
                    if (current.Length != 0 && current.Length + word.Length + 1 > maximumCharacters)
                    {
                        lines.Add(current);
                        current = word;
                    }
                    else current = current.Length == 0 ? word : current + " " + word;
                }
                lines.Add(current);
            }
            return lines.ToArray();
        }

        private static void DrawIngamePluginSettingsPage(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
        {
            Utils.DrawBorderString(spriteBatch, plugin.Name + " Settings", new Vector2(bounds.Center.X, bounds.Y + 16), Color.White, 0.9f, 0.5f, 0f, -1);
            var controls = _extensions.GetSettingsControls(plugin.Id);
            var pages = _extensions.GetSettingsPages(plugin.Id).Where(page => page.IsInteractive).ToArray();
            if (controls.Count == 0 && pages.Length == 0)
            {
                Utils.DrawBorderString(spriteBatch, "No plugin settings are available.", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.7f, 0.5f, 0.5f, -1);
                return;
            }

            int y = bounds.Y + 58;
            bool anySettingHovered = false;
            foreach (var control in controls)
            {
                y += DrawIngameTypedControl(spriteBatch, bounds, y, control, ref anySettingHovered);
            }
            foreach (var page in pages)
            {
                var hitArea = new Rectangle(bounds.X + 18, y - 9, bounds.Width - 36, 26);
                bool hovered = hitArea.Contains(Main.mouseX, Main.mouseY);
                anySettingHovered |= hovered;
                if (hovered && _ingameHoveredSettingId != page.Id)
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                if (hovered)
                    _ingameHoveredSettingId = page.Id;

                string value = ReadSettingValue(page);
                Utils.DrawBorderString(
                    spriteBatch,
                    page.DisplayName + ": " + value,
                    new Vector2(bounds.Center.X, y),
                    hovered ? new Color(255, 230, 140) : Color.White,
                    hovered ? 0.8f : 0.7f,
                    0.5f,
                    0f,
                    -1);

                if (hovered && Main.mouseLeft && Main.mouseLeftRelease)
                    ActivateSetting(page);
                y += 30;
            }

            if (!anySettingHovered)
                _ingameHoveredSettingId = null;
        }

        private static int DrawIngameTypedControl(SpriteBatch spriteBatch, Rectangle bounds, int y, PluginSettingControl control, ref bool anyHovered)
        {
            if (control.Kind == PluginSettingControlKind.Color)
            {
                var swatch = new Rectangle(bounds.X + 18, y - 7, 20, 20);
                Utils.DrawInvBG(spriteBatch, swatch.X, swatch.Y, swatch.Width, swatch.Height, new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue));
                Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + control.GetColor().ToHex(), new Vector2(bounds.X + 46, y), Color.White, 0.7f, 0f, 0f, -1);
                var copy = new Rectangle(bounds.Right - 73, y - 8, 25, 22);
                var paste = new Rectangle(bounds.Right - 42, y - 8, 25, 22);
                bool copyHover = copy.Contains(Main.mouseX, Main.mouseY), pasteHover = paste.Contains(Main.mouseX, Main.mouseY);
                anyHovered |= copyHover || pasteHover;
                DrawIngameClipboardButton(spriteBatch, copy, "Images/UI/CharCreation/Copy", copyHover, "Copy color hex");
                DrawIngameClipboardButton(spriteBatch, paste, "Images/UI/CharCreation/Paste", pasteHover, "Paste color hex");
                if (Main.mouseLeft && Main.mouseLeftRelease && copyHover) TrySetClipboardText(control.GetColor().ToHex());
                if (Main.mouseLeft && Main.mouseLeftRelease && pasteHover && PluginColor.TryParseHex(TryGetClipboardText(), out var pasted)) control.SetColor(pasted);
                return 34;
            }
            if (control.Kind == PluginSettingControlKind.Slider)
            {
                var bar = new Rectangle(bounds.Right - 150, y - 5, 132, 14);
                bool hovered = bar.Contains(Main.mouseX, Main.mouseY);
                anyHovered |= hovered;
                DrawIngameSlider(spriteBatch, bar, NormalizeSlider(control));
                Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + ReadSettingValue(control), new Vector2(bounds.X + 18, y), Color.White, 0.7f, 0f, 0f, -1);
                if (hovered && Main.mouseLeft) control.SetSlider(DenormalizeSlider((Main.mouseX - bar.X) / (float)bar.Width, control));
                return 32;
            }
            var hitArea = new Rectangle(bounds.X + 18, y - 9, bounds.Width - 36, 26);
            bool hover = hitArea.Contains(Main.mouseX, Main.mouseY);
            anyHovered |= hover;
            if (hover && _ingameHoveredSettingId != control.Id) SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            if (hover) _ingameHoveredSettingId = control.Id;
            Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + ReadSettingValue(control), new Vector2(bounds.Center.X, y), hover ? new Color(255, 230, 140) : Color.White, hover ? 0.8f : 0.7f, 0.5f, 0f, -1);
            if (hover && Main.mouseLeft && Main.mouseLeftRelease) ActivateSetting(control);
            return 30;
        }

        private static void DrawIngameSlider(SpriteBatch spriteBatch, Rectangle bar, float value)
        {
            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture == null) return;
            spriteBatch.Draw(_ingameBlankTexture, bar, new Color(29, 36, 70, 220));
            int filledWidth = (int)((bar.Width - 4) * MathHelper.Clamp(value, 0f, 1f));
            spriteBatch.Draw(_ingameBlankTexture, new Rectangle(bar.X + 2, bar.Y + 4, filledWidth, 6), new Color(160, 180, 255));
            spriteBatch.Draw(_ingameBlankTexture, new Rectangle(bar.X + 2 + filledWidth, bar.Y + 1, 4, 12), Color.White);
        }

        private static void DrawIngameClipboardButton(SpriteBatch spriteBatch, Rectangle bounds, string texturePath, bool hovered, string hoverText)
        {
            DrawResourcePackButtonBackground(spriteBatch, bounds, hovered);
            Texture2D texture = RequestTextureValue(texturePath);
            var destination = new Rectangle(bounds.Center.X - 7, bounds.Center.Y - 7, 14, 14);
            spriteBatch.Draw(texture, destination, Color.White);
            if (hovered) Main.instance.MouseText(hoverText);
        }

        private static float NormalizeSlider(PluginSettingControl control) => MathHelper.Clamp((control.GetSlider() - control.Minimum) / (control.Maximum - control.Minimum), 0f, 1f);
        private static float DenormalizeSlider(float value, PluginSettingControl control)
        {
            float result = control.Minimum + MathHelper.Clamp(value, 0f, 1f) * (control.Maximum - control.Minimum);
            return control.Step <= 0f ? result : control.Minimum + (float)Math.Round((result - control.Minimum) / control.Step) * control.Step;
        }

        private static bool HasSettings(PluginId pluginId) => _extensions.GetSettingsControls(pluginId).Count != 0 || _extensions.GetSettingsPages(pluginId).Any(page => page.IsInteractive);

        private static string ReadSettingValue(PluginSettingControl control)
        {
            try
            {
                switch (control.Kind)
                {
                    case PluginSettingControlKind.Toggle: return control.GetToggle() ? "Enabled" : "Disabled";
                    case PluginSettingControlKind.Cycle: return control.GetCycle();
                    case PluginSettingControlKind.Slider:
                        float value = control.GetSlider();
                        return control.FormatSlider == null ? value.ToString("0.##") : control.FormatSlider(value);
                    case PluginSettingControlKind.Color: return control.GetColor().ToHex();
                    default: return "Unavailable";
                }
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Read plugin setting", exception);
                return "Unavailable";
            }
        }

        private static void ActivateSetting(PluginSettingControl control)
        {
            try
            {
                if (control.Kind == PluginSettingControlKind.Toggle) control.SetToggle(!control.GetToggle());
                else if (control.Kind == PluginSettingControlKind.Cycle)
                {
                    var values = control.CycleValues;
                    int index = Array.IndexOf(values.ToArray(), control.GetCycle());
                    control.SetCycle(values[(index + 1 + values.Count) % values.Count]);
                }
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            catch (Exception exception) { Main.instance.MouseText("Unable to change plugin setting: " + exception.Message); }
        }

        private static string ReadSettingValue(PluginUiContribution contribution)
        {
            try { return contribution.ValueText == null ? string.Empty : contribution.ValueText(); }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Read legacy plugin setting", exception);
                return "Unavailable";
            }
        }

        private static void ActivateSetting(PluginUiContribution contribution)
        {
            try
            {
                contribution.Activate?.Invoke();
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            catch (Exception exception)
            {
                Main.instance.MouseText("Unable to change plugin setting: " + exception.Message);
            }
        }

        // Mirrors the character creator's ReLogic clipboard contract without creating a hard compile-time dependency.
        private static string TryGetClipboardText()
        {
            try
            {
                Type platform = Type.GetType("ReLogic.OS.Platform, ReLogic", false);
                Type clipboard = Type.GetType("ReLogic.OS.IClipboard, ReLogic", false);
                MethodInfo get = platform == null || clipboard == null ? null : platform.GetMethod("Get", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(clipboard);
                object service = get == null ? null : get.Invoke(null, null);
                PropertyInfo value = service == null ? null : service.GetType().GetProperty("Value");
                return value == null ? string.Empty : value.GetValue(service, null) as string ?? string.Empty;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Read clipboard", exception);
                return string.Empty;
            }
        }

        private static void TrySetClipboardText(string text)
        {
            try
            {
                Type platform = Type.GetType("ReLogic.OS.Platform, ReLogic", false);
                Type clipboard = Type.GetType("ReLogic.OS.IClipboard, ReLogic", false);
                MethodInfo get = platform == null || clipboard == null ? null : platform.GetMethod("Get", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(clipboard);
                object service = get == null ? null : get.Invoke(null, null);
                service?.GetType().GetProperty("Value")?.SetValue(service, text, null);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Write clipboard", exception);
            }
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
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Draw in-game icon", exception);
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
            object assets = GetMainAssets();
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

        internal static Texture2D RequestApprovedTexture(string path) => RequestTextureValue(path);

        internal static int? GetCurrentPing()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextPingSampleUtc)
                return _cachedPing;
            _nextPingSampleUtc = now.AddMilliseconds(250);
            try
            {
                if (!_pingLookupAttempted)
                {
                    _pingLookupAttempted = true;
                    Type pingType = Type.GetType("Terraria.Net.Ping, Terraria", throwOnError: false);
                    _currentPingProperty = pingType?.GetProperty("CurrentPing", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }

                object value = _currentPingProperty?.GetValue(null, null);
                _cachedPing = value is int ping ? ping : (int?)null;
                return _cachedPing;
            }
            catch
            {
                _cachedPing = null;
                return null;
            }
        }

        private static object GetMainAssets()
        {
            _mainAssetsField = _mainAssetsField ?? typeof(Main).GetField("Assets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (_mainAssetsField == null)
                throw new MissingFieldException(typeof(Main).FullName, "Assets");

            object owner = _mainAssetsField.IsStatic ? null : Main.instance;
            object assets = _mainAssetsField.GetValue(owner);
            if (assets == null)
                throw new InvalidOperationException("Terraria Main.Assets is unavailable.");
            return assets;
        }

        private static void ReportOptionalUiFailure(string operation, Exception exception)
        {
            string key = operation + ": " + exception.GetType().FullName + ": " + exception.Message;
            if (ReportedOptionalUiFailures.Add(key))
                Debug.WriteLine("Alacrity optional UI feature failed: " + key);
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

        private static void UpdateIngameDescriptionScroll(Rectangle bounds, int contentHeight, int visibleHeight)
        {
            if (bounds.Contains(Main.mouseX, Main.mouseY))
            {
                int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
                if (delta != 0) _ingameDescriptionScroll -= Math.Sign(delta) * 30f;
            }
            _ingameDescriptionScroll = Math.Max(0f, Math.Min(_ingameDescriptionScroll, Math.Max(0, contentHeight - visibleHeight)));
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

        private static void DrawIngameDescriptionScrollbar(SpriteBatch spriteBatch, Rectangle bounds, int contentHeight, int visibleHeight)
        {
            if (contentHeight <= visibleHeight) return;
            int trackX = bounds.Right - 13;
            int trackY = bounds.Y + 82;
            int trackHeight = visibleHeight;
            int thumbHeight = Math.Max(16, (int)(trackHeight * (visibleHeight / (float)contentHeight)));
            float maxScroll = contentHeight - visibleHeight;
            int thumbY = trackY + (int)((trackHeight - thumbHeight) * (_ingameDescriptionScroll / maxScroll));
            Utils.DrawInvBG(spriteBatch, trackX, trackY, 5, trackHeight, ResourcePackBorder);
            Utils.DrawInvBG(spriteBatch, trackX, thumbY, 5, thumbHeight, ResourcePackHoverBackground);
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
            _extensions = new PluginExtensionHost();
            _serviceHub = new PluginServiceHub();
            _chat = new PluginChatHost();
            var overlays = new PluginOverlayHost();
            _userInteraction = new PluginUserInteractionHost(new TerrariaPluginUserInteractionBackend());
            var contexts = new PluginHostContextFactory(root, _serviceHub, _extensions, new PluginCommandHost(), overlays, _chat, _userInteraction);
            var runtimeHost = new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), contexts);
            var activation = new PluginActivationCoordinator(patchHost, new PluginEnablePlanner(), new PluginEnableExecutor(_notifications), new PluginActivationGate(_diagnostics));
            _runtime = new PluginManagerRuntime(runtimeHost, new PluginPackageLifecycleRegistry(), activation);
            _menu = new PluginManagementMenu(_runtime);
        }

        private static IPluginUserInteractionService GetBetterChatUserInteraction()
        {
            if (_betterChatUserInteraction != null)
                return _betterChatUserInteraction;

            var record = _runtime == null ? null : _runtime.Registry.Records.FirstOrDefault(candidate => candidate.Manifest.Id == BetterChatPluginId);
            return record == null || _userInteraction == null
                ? new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance).CreateService(new PluginManifest(new PluginId("alacrity.unavailable"), "Unavailable", new Version(1, 0), "Alacrity", "Unavailable", new[] { "1.4.5.6" }))
                : (_betterChatUserInteraction = _userInteraction.CreateService(record.Manifest));
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
            RestoreEnabledPlugins();
        }

        private static void EnsureChatRuntime()
        {
            BootstrapPluginRuntime();
        }

        private static string PluginStatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugin-state.json");
        private static string LegacyEnabledPluginsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "enabled-plugins.txt");

        private static void RestoreEnabledPlugins()
        {
            if (_enabledStateRestored) return;
            _enabledStateRestored = true;
            try
            {
                var requested = ReadEnabledPluginIds();
                foreach (var record in _runtime.Registry.Records.Where(record => requested.Contains(record.Manifest.Id.Value) && (record.State == PluginPackageLifecycleState.Loaded || record.State == PluginPackageLifecycleState.Disabled)).ToArray())
                    EnablePlugin(record.Manifest.Id);
                if (!File.Exists(PluginStatePath) && File.Exists(LegacyEnabledPluginsPath))
                    PersistEnabledPlugins();
            }
            catch (Exception exception)
            {
                _notifications.Publish("Unable to restore enabled plugins: " + exception.Message, TimeSpan.FromSeconds(4));
            }
        }

        private static void EnablePlugin(PluginId id)
        {
            var record = _runtime.Registry.Records.Single(record => record.Manifest.Id == id);
            if (record.Controller != null && record.Controller.UsesAsyncLifecycle)
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                _runtime.EnableAsync(id, cancellation.Token).GetAwaiter().GetResult();
            }
            else
                _runtime.Enable(id);
        }

        private static void DisablePlugin(PluginId id)
        {
            var record = _runtime.Registry.Records.Single(record => record.Manifest.Id == id);
            if (record.Controller != null && record.Controller.UsesAsyncLifecycle)
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                _runtime.DisableAsync(id, cancellation.Token).GetAwaiter().GetResult();
            }
            else
                _runtime.Disable(id);
        }

        private static bool BeginPluginOperation(PluginId id, bool enable, out string error)
        {
            error = string.Empty;
            if (PendingPluginOperations.ContainsKey(id))
            {
                error = "Plugin operation is already in progress.";
                return false;
            }

            var record = _runtime.Registry.Records.Single(record => record.Manifest.Id == id);
            if (record.Controller == null || !record.Controller.UsesAsyncLifecycle)
            {
                if (enable)
                    EnablePlugin(id);
                else
                    DisablePlugin(id);
                PersistEnabledPlugins();
                return true;
            }

            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            Task task = enable ? _runtime.EnableAsync(id, cancellation.Token) : _runtime.DisableAsync(id, cancellation.Token);
            PendingPluginOperations.Add(id, new PendingPluginOperation(enable, task, cancellation));
            return true;
        }

        private static bool CompletePluginOperations()
        {
            bool changed = false;
            foreach (PluginId id in PendingPluginOperations.Where(pair => pair.Value.Task.IsCompleted).Select(pair => pair.Key).ToArray())
            {
                PendingPluginOperation operation = PendingPluginOperations[id];
                PendingPluginOperations.Remove(id);
                operation.Cancellation.Dispose();
                try
                {
                    operation.Task.GetAwaiter().GetResult();
                    PersistEnabledPlugins();
                    _notifications?.Publish((operation.Enable ? "Enabled " : "Disabled ") + id.Value + ".", TimeSpan.FromSeconds(4));
                }
                catch (Exception exception)
                {
                    _notifications?.Publish("Unable to " + (operation.Enable ? "enable " : "disable ") + id.Value + ": " + exception.Message, TimeSpan.FromSeconds(4));
                }
                changed = true;
            }
            return changed;
        }

        private static IEnumerable<PluginPackageRuntimeRecord> GetShutdownOrder()
        {
            if (_runtime == null) return Array.Empty<PluginPackageRuntimeRecord>();
            var records = _runtime.Registry.Records.ToDictionary(record => record.Manifest.Id);
            var visited = new HashSet<PluginId>();
            var dependencyFirst = new List<PluginPackageRuntimeRecord>();
            foreach (var record in records.Values)
                Visit(record);
            dependencyFirst.Reverse();
            return dependencyFirst;

            void Visit(PluginPackageRuntimeRecord record)
            {
                if (!visited.Add(record.Manifest.Id)) return;
                foreach (var dependency in record.Manifest.Dependencies)
                    if (records.TryGetValue(dependency.Id, out var dependencyRecord)) Visit(dependencyRecord);
                dependencyFirst.Add(record);
            }
        }

        private static void PersistEnabledPlugins()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PluginStatePath));
                var records = _runtime.Registry.Records.Where(record => record.State != PluginPackageLifecycleState.Uninstalled).OrderBy(record => record.Manifest.Id.Value, StringComparer.Ordinal).ToArray();
                string json = "{\n  \"plugins\": [\n" + string.Join(",\n", records.Select(record => "    { \"id\": \"" + record.Manifest.Id.Value + "\", \"enabled\": " + (record.State == PluginPackageLifecycleState.Enabled ? "true" : "false") + " }")) + "\n  ]\n}\n";
                string temporaryPath = PluginStatePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Copy(temporaryPath, PluginStatePath, true);
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                _notifications.Publish("Unable to save enabled plugins: " + exception.Message, TimeSpan.FromSeconds(4));
            }
        }

        private static HashSet<string> ReadEnabledPluginIds()
        {
            if (File.Exists(PluginStatePath))
            {
                string json = File.ReadAllText(PluginStatePath);
                return new HashSet<string>(Regex.Matches(json, "\\\"id\\\"\\s*:\\s*\\\"([a-z0-9.-]+)\\\"\\s*,\\s*\\\"enabled\\\"\\s*:\\s*true", RegexOptions.CultureInvariant).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
            }
            return File.Exists(LegacyEnabledPluginsPath) ? new HashSet<string>(File.ReadAllLines(LegacyEnabledPluginsPath), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class KeybindControlsState
        {
            public long Version = -1;
        }

        private sealed class PendingPluginOperation
        {
            internal PendingPluginOperation(bool enable, Task task, CancellationTokenSource cancellation)
            {
                Enable = enable;
                Task = task;
                Cancellation = cancellation;
            }

            internal bool Enable { get; }
            internal Task Task { get; }
            internal CancellationTokenSource Cancellation { get; }
        }

        private sealed class PluginKeybindControlGroup : UISortableElement
        {
            public PluginKeybindControlGroup(int order) : base(order) { }
        }

        /// <summary>Native controls-list row with a verified plugin label and Terraria's own rebind flow.</summary>
        private sealed class PluginKeybindingListItem : UIElement
        {
            private readonly PluginKeybindRegistration keybind;
            private readonly InputMode mode;
            private readonly Color color;

            public PluginKeybindingListItem(PluginKeybindRegistration keybind, InputMode mode, Color color)
            {
                this.keybind = keybind;
                this.mode = mode;
                this.color = color;
                OnLeftClick += Listen;
            }

            private void Listen(UIMouseEvent _, UIElement __)
            {
                if (PlayerInput.CurrentProfile.AllowEditing)
                    PlayerInput.ListenFor(keybind.HostId, mode);
                else
                    PlayerInput.ListenFor(null, mode);
            }

            protected override void DrawSelf(SpriteBatch spriteBatch)
            {
                var dimensions = GetDimensions();
                EnsureInputBinding(keybind, mode);
                bool listening = PlayerInput.ListeningTrigger == keybind.HostId;
                var textColor = listening ? Color.Gold : (IsMouseHovering ? Color.White : Color.Silver);
                textColor = Color.Lerp(textColor, Color.White, IsMouseHovering ? 0.5f : 0f);
                var panelColor = IsMouseHovering ? color : color.MultiplyRGBA(new Color(180, 180, 180));
                var textScale = new Vector2(0.8f);
                Utils.DrawSettingsPanel(spriteBatch, dimensions.Position(), dimensions.Width + 1f, panelColor);
                var namePosition = dimensions.Position() + new Vector2(8f, 8f);
                Utils.DrawBorderString(spriteBatch, keybind.Descriptor.DisplayName, namePosition, textColor, textScale.X, 0f, 0f, -1);

                var bindings = PlayerInput.CurrentProfile.InputModes[mode].KeyStatus[keybind.HostId];
                ObservePluginKeybindBindings(keybind, mode, bindings);
                string bindingText = DescribeBindings(bindings, mode);
                if (string.IsNullOrEmpty(bindingText))
                {
                    bindingText = Lang.menu[195].Value;
                    if (!listening)
                        textColor = new Color(80, 80, 80);
                }

                var size = new Vector2(bindingText.Length * 11f * textScale.X, 18f * textScale.Y);
                var bindingPosition = new Vector2(dimensions.X + dimensions.Width - size.X - 10f, dimensions.Y + 8f);
                if (mode == InputMode.XBoxGamepad || mode == InputMode.XBoxGamepadUI)
                    bindingPosition.Y -= 3f;
                float previousGlyphScale = GlyphTagHandler.GlyphsScale;
                try
                {
                    GlyphTagHandler.GlyphsScale = 0.85f;
                    Utils.DrawBorderString(spriteBatch, bindingText, bindingPosition, textColor, textScale.X, 0f, 0f, -1);
                }
                finally
                {
                    GlyphTagHandler.GlyphsScale = previousGlyphScale;
                }
            }

            private static string DescribeBindings(IReadOnlyList<string> bindings, InputMode mode)
            {
                if (bindings.Count == 0)
                    return string.Empty;
                if (mode == InputMode.XBoxGamepad || mode == InputMode.XBoxGamepadUI)
                    return string.Join("/", bindings.Select(GlyphTagHandler.GenerateTag));
                return string.Join("/", bindings);
            }
        }

        private sealed class PluginSelectionMenu : UIState
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

                if (_addRemoveView)
                {
                    SetupPackageGamepadPoints(spriteBatch, allPoints, startId, ref nextId);
                    return;
                }

                var availablePoints = _availableList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, availablePoints, _availableList);
                var enabledPoints = _enabledList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, enabledPoints, _enabledList);

                var availableDescription = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "DescriptionOff");
                var availableToggle = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "ToggleToOn");
                var enabledDescription = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "DescriptionOn");
                var enabledSettings = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "SettingsOn");
                var enabledToggle = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "ToggleToOff");
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

            private void SetupPackageGamepadPoints(SpriteBatch spriteBatch, List<SnapPoint> allPoints, int startId, ref int nextId)
            {
                var packagePoints = _packageList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, packagePoints, _packageList);
                var description = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, packagePoints, "PackageDescription");
                var uninstall = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, packagePoints, "PackageUninstall");
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
                button.SetPadding(0f); AppendSmallIcon(button, "Images/UI/ButtonCloudInactive"); button.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText("Plugin is up to date"); }; return button;
            }

            private static UIResourcePackInfoButton<string> CreateUninstallButton()
            {
                var button = new UIResourcePackInfoButton<string>("", 0.8f, false); button.SetPadding(0f); AppendSmallIcon(button, "Images/UI/ButtonDelete"); button.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText("Uninstall Plugin"); }; return button;
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
                if (PendingPluginOperations.ContainsKey(plugin.Id))
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
                badge.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText(tooltip); };
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

        private sealed class PluginSettingsMenu : UIState
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

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
                SetupGamepadPoints(spriteBatch);
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
                    if (point.Name == "GoBack")
                        back = gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                gamepadHelper.LinkVerticalStripBottomSideToSingle(settings, back);
                gamepadHelper.MoveToVisuallyClosestPoint(firstId, nextId);
            }

        }

        private sealed class PluginDescriptionMenu : UIState
        {
            private readonly PluginManagerRow _plugin;
            private UIGamepadHelper gamepadHelper;

            public PluginDescriptionMenu(PluginManagerRow plugin)
            {
                _plugin = plugin;
            }

            public override void OnInitialize()
            {
                // Mirrors UIResourcePackInfoMenu so variable package text is measured and scrolls instead of clipping.
                var outer = new UIElement { Width = new StyleDimension(0f, 0.8f), MaxWidth = new StyleDimension(500f, 0f), MinWidth = new StyleDimension(300f, 0f), Top = new StyleDimension(230f, 0f), Height = new StyleDimension(-230f, 1f), HAlign = 0.5f };
                Append(outer);
                var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-110f, 1f), BackgroundColor = new Color(33, 43, 79) * 0.8f };
                outer.Append(panel);
                var content = new UIElement { Width = StyleDimension.Fill, Height = StyleDimension.Fill };
                panel.Append(content);

                var title = new UIText(_plugin.Name, 0.935f, true) {
                    HAlign = 0.5f,
                    Top = new StyleDimension(0f, 0f)
                };
                content.Append(title);

                var author = new UIText("Author: " + _plugin.Author, 0.8f, false) {
                    HAlign = 0f, VAlign = 0f, Top = new StyleDimension(42f, 0f)
                };
                content.Append(author);

                var version = new UIText("Version: " + _plugin.Version, 0.8f, false) {
                    HAlign = 1f, VAlign = 0f, Top = new StyleDimension(42f, 0f)
                };
                content.Append(version);

                var list = new UIList { Width = new StyleDimension(-25f, 1f), Height = new StyleDimension(-112f, 1f), VAlign = 1f, Top = new StyleDimension(-8f, 0f), ListPadding = 14f, PaddingRight = 12f, ManualSortMethod = items => { } };
                list.Add(CreateSection("Description", _plugin.Description, true));
                list.Add(CreateSection("Changelog", _plugin.Changelog, false));
                content.Append(list);
                var scrollbar = new UIScrollbar { Height = new StyleDimension(-112f, 1f), HAlign = 1f, VAlign = 1f, Top = new StyleDimension(-8f, 0f) };
                content.Append(scrollbar);
                list.SetScrollbar(scrollbar);

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
                back.SetSnapPoint("GoBack", 0, null, null);
                outer.Append(back);
            }

            private static UIElement CreateSection(string heading, string text, bool includeDivider)
            {
                string value = string.IsNullOrWhiteSpace(text) ? "No information provided." : text;
                int lines = Math.Max(1, (value.Length + 45) / 46);
                float bodyHeight = lines * 19f;
                float dividerSpace = includeDivider ? 14f : 0f;
                var section = new UIElement { Width = StyleDimension.Fill, Height = new StyleDimension(38f + bodyHeight + dividerSpace, 0f) };
                section.Append(new UIText(heading, 0.68f, true) { Width = StyleDimension.Fill, Height = new StyleDimension(20f, 0f) });
                // The description shifts up by eight pixels; the changelog body shifts up by four after its heading moves with the section.
                section.Append(new UIText(value, 0.75f, false) { Width = StyleDimension.Fill, Top = new StyleDimension(includeDivider ? 30f : 34f, 0f), IsWrapped = true, WrappedTextBottomPadding = 0f });
                return section;
            }

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
                int firstId = 3700;
                int nextId = firstId;
                foreach (var point in GetSnapPoints().Where(point => point.Name == "GoBack"))
                    gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                gamepadHelper.MoveToVisuallyClosestPoint(firstId, nextId);
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
