using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public static partial class PluginUiRuntime
    {
        private static PluginManagerRuntime _runtime;
        private static PluginManagementMenu _menu;
        private static PluginNotificationCenter _notifications;
        private static PluginDependencyDiagnostics _diagnostics;
        private static PluginExtensionHost _extensions;
        private static PluginServiceHub _serviceHub;
        private static PluginCommandHost _commands;
        private static TerrariaPluginDrawAdapter _drawAdapter;
        private static PluginDispatcherHost _dispatcher;
        private static TerrariaEntitySnapshotCache _entitySnapshots;
        private static TerrariaSessionPresentationService _sessionPresentation;
        private static PluginChatHost _chat;
        private static TerrariaPluginChatAdapter _chatAdapter;
        private static PluginUserInteractionHost _userInteraction;
        private static readonly PluginManagerPresenter _presenter = new PluginManagerPresenter();
        private static readonly Color ResourcePackBackground = new Color(26, 40, 89) * 0.8f;
        private static readonly Color ResourcePackBorder = new Color(13, 20, 44) * 0.8f;
        private static readonly Color ResourcePackHoverBackground = new Color(46, 60, 119);
        private static readonly Color ResourcePackHoverBorder = new Color(20, 30, 56);
        private static MethodInfo _assetRequest;
        private static MethodInfo _assetFrame;
        private static PropertyInfo _assetValue;
        private static FieldInfo _mainAssetsField;
        private static readonly HashSet<string> ReportedOptionalUiFailures = new HashSet<string>(StringComparer.Ordinal);
        private static Texture2D _ingameBlankTexture;
        private static GraphicsDevice _ingameBlankTextureDevice;
        private static bool _pluginMenuOpen;
        private static PluginSelectionMenu _selectionMenu;
        private static PluginManagerRow[] _ingameEntries = Array.Empty<PluginManagerRow>();
        private static int _ingameSelectedEntry;
        private static int _ingameView;
        private static float _ingameScroll;
        private static float _ingameDescriptionScroll;
        private static string _ingameHoveredSettingId;
        private static TerrariaPluginEnabledStateStore _enabledStateStore;
        private static readonly object RuntimeGate = new object();
        private static PluginVisualEffectsHost _visualEffects;
        private static TerrariaVisualEffectsRuntimePolicy _visualEffectsPolicy = TerrariaVisualEffectsRuntimePolicy.Vanilla;
        private static PluginVisualEffectsPolicy _lastVisualEffectsSnapshot;
        private static bool _runtimeBootstrapped;
        private static bool _runtimeShuttingDown;
        private static readonly Dictionary<string, bool> KeybindDownState = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static TerrariaPluginOperationCoordinator _pluginOperations;
        private static long _keybindRegistryVersion = -1;
        private static PlayerInputProfile _nativeKeybindProfile;
        private static long _nativeKeybindRegistryVersion = -1;
        private static readonly HashSet<string> NativePluginKeybindIds = new HashSet<string>(StringComparer.Ordinal);
        private static TerrariaPluginKeybindPersistence _keybindPersistence;
        private static TerrariaPluginKeybindControlsAdapter _keybindControlsAdapter;
        private static uint _iconInteractionInputTick = uint.MaxValue;
        private static bool _iconInteractionWasDown;
        private static bool _iconInteractionPressed;
        private static bool _iconInteractionConsumed;

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
                if (_runtime != null)
                {
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
                _drawAdapter?.Dispose();
                _pluginOperations?.CancelAll();
                _ingameBlankTexture?.Dispose();
                _ingameBlankTexture = null;
                _ingameBlankTextureDevice = null;
            }
        }

        /// <summary>Returns whether an enabled plugin owns a chat editor. The injected hook calls this only while player chat is focused.</summary>
        public static bool IsBetterChatActive()
        {
            return HasChatInputEditors();
        }

        /// <summary>Registration-driven chat-editor availability for all chat plugins.</summary>
        public static bool HasChatInputEditors()
        {
            return _chatAdapter != null && _chatAdapter.HasInputEditors();
        }

        /// <summary>Processes player-chat input through the enabled scoped chat editor registrations.</summary>
        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            return ProcessChatInput(text, allowMultiLine);
        }

        /// <summary>Processes player chat through the active generic editor pipeline.</summary>
        public static string ProcessChatInput(string text, bool allowMultiLine)
        {
            return _chatAdapter == null ? text : _chatAdapter.ProcessInput(text, allowMultiLine);
        }

        /// <summary>Consumes only registered local plugin commands before Terraria creates an outgoing chat packet.</summary>
        public static bool TryHandlePluginChatCommand(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || text[0] != '/')
                    return false;
                BootstrapPluginRuntime();
                if (_commands == null)
                    return false;
                string[] parts = text.Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return false;
                var arguments = new string[Math.Max(0, parts.Length - 1)];
                if (arguments.Length > 0)
                    Array.Copy(parts, 1, arguments, 0, arguments.Length);
                return _commands.Dispatch(parts[0], arguments, ShowPluginCommandReply) != PluginCommandDispatchResult.NotFound;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin chat command", exception);
                return false;
            }
        }

        private static void ShowPluginCommandReply(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Main.NewText(message, 190, 220, 255);
        }

        /// <summary>Creates draw-only chat markup. It never modifies Main.chatText or outgoing packet text.</summary>
        public static string FormatPlayerChatText(string text)
        {
            return FormatChatInputForDraw(text);
        }

        /// <summary>Formats focused player-chat input through the generic chat extension runtime.</summary>
        public static string FormatChatInputForDraw(string text)
        {
            return _chatAdapter == null ? text : _chatAdapter.FormatInputForDraw(text);
        }

        /// <summary>Decorates parsed normal chat snippets outside the draw loop.</summary>
        public static object DecorateChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            return _chatAdapter == null ? snippets : _chatAdapter.Decorate(snippets, baseColor, originalMessage);
        }

        /// <summary>Filters network chat before Terraria creates overhead or scrolling-chat entries.</summary>
        public static bool ShouldDisplayNetworkChatMessage(byte messageAuthor)
        {
            return _chatAdapter == null || _chatAdapter.ShouldDisplayNetworkMessage(messageAuthor);
        }

        /// <summary>Filters client-originated system messages without affecting network receive behavior.</summary>
        public static bool ShouldDisplayLocalChatMessage()
        {
            return _chatAdapter == null || _chatAdapter.ShouldDisplayLocalMessage();
        }

        /// <summary>Shows bounded vanilla-style hover feedback and handles copy-on-right-click.</summary>
        public static void HandleChatSnippetHover(object snippet)
        {
            if (_chatAdapter != null) _chatAdapter.HandleHover(snippet);
        }

        /// <summary>Activates only validated http or https links registered by an enabled plugin.</summary>
        public static bool HandleChatSnippetClick(object snippet)
        {
            return _chatAdapter != null && _chatAdapter.HandleClick(snippet);
        }

        /// <summary>Applies the current hover highlight without mutating the original snippet color.</summary>
        public static Color GetChatSnippetVisibleColor(object snippet, Color color)
        {
            return _chatAdapter == null ? color : _chatAdapter.GetVisibleColor(snippet, color);
        }

        /// <summary>Transfers parse-time line ownership when Terraria clones a snippet during layout.</summary>
        public static void CopyChatSnippetContext(object source, object copy)
        {
            if (_chatAdapter != null) _chatAdapter.CopySnippetContext(source, copy);
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
            _drawAdapter?.DrawNotifications(spriteBatch);
        }

        /// <summary>Draws host-validated diagnostics overlays without exposing mutable Terraria state to plugins.</summary>
        public static void DrawHitboxes(SpriteBatch spriteBatch)
        {
            DrawWorldOverlays(spriteBatch);
        }

        /// <summary>Dispatches framework-neutral plugin world overlays at Terraria's verified world UI phase.</summary>
        public static void DrawWorldOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawWorldOverlays(spriteBatch);
        }

        /// <summary>Dispatches screen-space gameplay HUD overlays through Terraria's established UI SpriteBatch.</summary>
        public static void DrawHudOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawHudOverlays(spriteBatch);
        }

        /// <summary>Dispatches menu-space overlays from Terraria's menu SpriteBatch after version text is drawn.</summary>
        public static void DrawMenuOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawMenuOverlays(spriteBatch);
        }

        /// <summary>Captures Terraria's already-computed melee collision rectangle for presentation on the next draw.</summary>
        public static void CaptureSwingHitbox(Player player, bool dontAttack, Rectangle hitbox)
        {
            CaptureMeleeCollisionBounds(player, dontAttack, hitbox);
        }

        /// <summary>Captures host-computed melee collision bounds for active generic presentation consumers.</summary>
        public static void CaptureMeleeCollisionBounds(Player player, bool dontAttack, Rectangle hitbox)
        {
            CombatPresentationRuntime.CaptureSwingHitbox(player, dontAttack, hitbox);
        }

        /// <summary>Compatibility entry point retained for the existing version-locked HUD patch.</summary>
        public static void DrawPlayerList(SpriteBatch spriteBatch)
        {
            DrawHudWidgets(spriteBatch);
        }

        /// <summary>Dispatches generic retained HUD widgets without knowing which plugin provided them.</summary>
        public static void DrawHudWidgets(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawHudWidgets(spriteBatch);
        }

        /// <summary>
        /// Appends verified plugin bindings to Terraria's native controls list. The controls adapter
        /// is deliberately optional: a changed UI signature leaves vanilla controls untouched.
        /// </summary>
        public static void AppendPluginKeybindControls(UIManageControls controls)
        {
            if (Volatile.Read(ref _runtimeBootstrapped) && controls != null) _keybindControlsAdapter?.Append(controls);
        }

        private static void EnsureInputBinding(PluginKeybindRegistration keybind, InputMode mode)
        {
            var configuration = PlayerInput.CurrentProfile.InputModes[mode];
            if (!configuration.KeyStatus.ContainsKey(keybind.HostId))
                configuration.KeyStatus.Add(keybind.HostId, _keybindPersistence.GetBindings(keybind, mode, PlayerInput.CurrentProfile?.Name));
        }

        private static void ObservePluginKeybindBindings(PluginKeybindRegistration keybind, InputMode mode, IReadOnlyList<string> bindings)
        {
            _keybindPersistence.Observe(keybind, mode, PlayerInput.CurrentProfile?.Name, bindings);
        }

        /// <summary>
        /// Polls host-owned plugin bindings at the established gameplay UI boundary. The native
        /// input profile remains the source of persisted bindings; no plugin receives raw input.
        /// </summary>
        public static void UpdatePluginKeybinds()
        {
            if (!Volatile.Read(ref _runtimeBootstrapped) || Volatile.Read(ref _runtimeShuttingDown) || _extensions == null)
                return;
            try
            {
                _entitySnapshots?.CaptureForCurrentTick();
                _sessionPresentation?.CaptureForCurrentTick();
                UpdateIconInteractionInput();
                _dispatcher?.Drain(exception => ReportOptionalUiFailure("Plugin dispatcher callback", exception));
                RefreshVisualEffectsPolicy();
                if (Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.blockInput)
                    return;
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

        /// <summary>
        /// Synchronizes plugin IDs into Terraria's native trigger dictionaries before KeyboardInput
        /// calls KeyConfiguration.CopyKeyState. Terraria indexes those dictionaries directly.
        /// </summary>
        public static void EnsurePluginKeybindStateShape()
        {
            if (!Volatile.Read(ref _runtimeBootstrapped) || _extensions == null || PlayerInput.CurrentProfile == null)
                return;

            try
            {
                PluginKeybindRegistrySnapshot snapshot = _extensions.GetKeybindSnapshot();
                PlayerInputProfile profile = PlayerInput.CurrentProfile;
                if (ReferenceEquals(profile, _nativeKeybindProfile) && snapshot.Version == _nativeKeybindRegistryVersion)
                    return;

                var activeIds = new HashSet<string>(snapshot.Registrations.Select(keybind => keybind.HostId), StringComparer.Ordinal);
                foreach (var configuration in profile.InputModes.Values)
                    foreach (string staleId in NativePluginKeybindIds.Where(id => !activeIds.Contains(id)).ToArray())
                        configuration.KeyStatus.Remove(staleId);

                RemoveStaleTriggerKeys(NativePluginKeybindIds, activeIds);
                foreach (PluginKeybindRegistration keybind in snapshot.Registrations)
                {
                    EnsureInputBinding(keybind, InputMode.Keyboard);
                    EnsureTriggerKey(PlayerInput.Triggers.Current, keybind.HostId);
                    EnsureTriggerKey(PlayerInput.Triggers.Old, keybind.HostId);
                    EnsureTriggerKey(PlayerInput.Triggers.JustPressed, keybind.HostId);
                    EnsureTriggerKey(PlayerInput.Triggers.JustReleased, keybind.HostId);
                }

                NativePluginKeybindIds.Clear();
                NativePluginKeybindIds.UnionWith(activeIds);
                _nativeKeybindProfile = profile;
                _nativeKeybindRegistryVersion = snapshot.Version;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin keybind state synchronization", exception);
            }
        }

        private static void EnsureTriggerKey(TriggersSet triggers, string key)
        {
            if (!triggers.KeyStatus.ContainsKey(key))
                triggers.KeyStatus.Add(key, false);
        }

        private static void RemoveStaleTriggerKeys(IEnumerable<string> knownIds, ISet<string> activeIds)
        {
            foreach (string id in knownIds.Where(id => !activeIds.Contains(id)).ToArray())
            {
                PlayerInput.Triggers.Current.KeyStatus.Remove(id);
                PlayerInput.Triggers.Old.KeyStatus.Remove(id);
                PlayerInput.Triggers.JustPressed.KeyStatus.Remove(id);
                PlayerInput.Triggers.JustReleased.KeyStatus.Remove(id);
            }
        }

        /// <summary>Whole-system Dust fast path used before Terraria enters the DrawDust or UpdateDust loops.</summary>
        public static bool ShouldRunDustSystem() => _visualEffectsPolicy.DustEffectsEnabled || _visualEffectsPolicy.HasExceptions;

        /// <summary>Creation gate for Dust.NewDust. Exceptions remain live when ordinary Dust is disabled.</summary>
        public static bool ShouldCreateDust(int dustType) => _visualEffectsPolicy.DustEffectsEnabled || _visualEffectsPolicy.ContainsDustException(dustType);

        /// <summary>Per-instance Dust update gate used only when exceptions require the Dust loop to run.</summary>
        public static bool ShouldUpdateDustInstance(Dust dust) => dust != null && ShouldCreateDust(dust.type);

        /// <summary>Per-instance Dust draw gate used only when exceptions require DrawDust to run.</summary>
        public static bool ShouldDrawDustInstance(Dust dust) => dust != null && ShouldCreateDust(dust.type);

        /// <summary>Whole-system Gore gate. Gore has no exception path.</summary>
        public static bool ShouldRunGoreSystem() => _visualEffectsPolicy.GoreEffectsEnabled;

        private static void RefreshVisualEffectsPolicy()
        {
            if (_visualEffects == null)
            {
                _visualEffectsPolicy = TerrariaVisualEffectsRuntimePolicy.Vanilla;
                _lastVisualEffectsSnapshot = null;
                return;
            }

            try
            {
                PluginVisualEffectsPolicy snapshot = _visualEffects.GetEffectivePolicy();
                if (ReferenceEquals(snapshot, _lastVisualEffectsSnapshot))
                    return;
                _visualEffectsPolicy = TerrariaVisualEffectsRuntimePolicy.Create(snapshot);
                _lastVisualEffectsSnapshot = snapshot;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Dust & Gore Toggle policy", exception);
                _visualEffectsPolicy = TerrariaVisualEffectsRuntimePolicy.Vanilla;
                _lastVisualEffectsSnapshot = null;
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
            _keybindPersistence.Prune(active);
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

        /// <summary>Resolves a scoped icon interaction for a Terraria-owned immediate-mode surface.</summary>
        internal static PluginIconInteractionState EvaluateIconInteraction(PluginId owner, string id, PluginUiRect bounds)
        {
            PluginExtensionHost extensions = _extensions;
            return extensions == null ? default(PluginIconInteractionState) : extensions.EvaluateIconInteraction(owner, id, bounds, Main.mouseX, Main.mouseY);
        }

        /// <summary>Invokes one hovered scoped icon action for the current primary-pointer press.</summary>
        internal static bool TryActivateIconInteraction(PluginId owner, string id, PluginUiRect bounds)
        {
            PluginExtensionHost extensions = _extensions;
            if (extensions == null || _iconInteractionConsumed || !_iconInteractionPressed || !bounds.Contains(Main.mouseX, Main.mouseY))
                return false;
            bool activated = extensions.TryActivateIconInteraction(owner, id);
            if (activated)
            {
                _iconInteractionConsumed = true;
                Main.mouseLeftRelease = false;
            }
            return activated;
        }

        private static void UpdateIconInteractionInput()
        {
            uint tick = Main.GameUpdateCount;
            if (tick == _iconInteractionInputTick)
                return;
            bool down = Main.mouseLeft;
            _iconInteractionPressed = down && !_iconInteractionWasDown;
            _iconInteractionWasDown = down;
            _iconInteractionConsumed = false;
            _iconInteractionInputTick = tick;
        }

        /// <summary>Draws a host-owned tooltip without exposing Terraria rendering types through the SDK.</summary>
        internal static void DrawIconTooltip(SpriteBatch spriteBatch, PluginIconInteractionState state)
        {
            if (spriteBatch == null || !state.IsHovered || state.Tooltip == null)
                return;
            PluginTooltipOptions tooltip = state.Tooltip;
            Vector2 position = new Vector2(Main.mouseX, Main.mouseY);
            float originX = 0f;
            float originY = 0f;
            switch (tooltip.Placement)
            {
                case PluginTooltipPlacement.Left:
                    position.X -= 16f; originX = 1f; originY = 0.5f; break;
                case PluginTooltipPlacement.Right:
                    position.X += 16f; originY = 0.5f; break;
                case PluginTooltipPlacement.Above:
                    position.Y -= 16f; originX = 0.5f; originY = 1f; break;
                case PluginTooltipPlacement.Below:
                    position.Y += 16f; originX = 0.5f; break;
                default:
                    position.X += 16f; position.Y += 16f; break;
            }
            PluginColor color = tooltip.Color ?? new PluginColor(255, 255, 255);
            Utils.DrawBorderString(spriteBatch, tooltip.Text, position, new Color(color.Red, color.Green, color.Blue), tooltip.Scale, originX, originY, -1);
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
            if (spriteBatch == null)
                return;
            if (_ingameBlankTexture != null && !_ingameBlankTexture.IsDisposed && ReferenceEquals(_ingameBlankTextureDevice, spriteBatch.GraphicsDevice)) return;

            try
            {
                _ingameBlankTexture?.Dispose();
                _ingameBlankTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1);
                _ingameBlankTexture.SetData(new[] { Color.White });
                _ingameBlankTextureDevice = spriteBatch.GraphicsDevice;
            }
            catch
            {
                _ingameBlankTexture = null;
                _ingameBlankTextureDevice = null;
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
            TerrariaPluginRuntimeServices services = TerrariaPluginRuntimeServices.Create(root);
            _runtime = services.Runtime;
            _menu = services.Menu;
            _notifications = services.Notifications;
            _diagnostics = services.Diagnostics;
            _extensions = services.Extensions;
            _serviceHub = services.ServiceHub;
            _commands = services.Commands;
            _drawAdapter = new TerrariaPluginDrawAdapter(services.Notifications, services.Overlays, services.Hud, services.HudAdapter, services.EntitySnapshots, ReportOptionalUiFailure);
            _dispatcher = services.Dispatcher;
            _entitySnapshots = services.EntitySnapshots;
            _sessionPresentation = services.SessionPresentation;
            _chat = services.Chat;
            _userInteraction = services.UserInteraction;
            _chatAdapter = new TerrariaPluginChatAdapter(_chat, EnsureChatRuntime, GetActiveChatUserInteraction, ReportOptionalUiFailure);
            _visualEffects = services.VisualEffects;
            _enabledStateStore = new TerrariaPluginEnabledStateStore(root);
            _keybindPersistence = new TerrariaPluginKeybindPersistence(root, ReportOptionalUiFailure);
            _keybindControlsAdapter = new TerrariaPluginKeybindControlsAdapter(_extensions, EnsureInputBinding, ObservePluginKeybindBindings, ReportOptionalUiFailure);
            _pluginOperations = new TerrariaPluginOperationCoordinator(_runtime, PersistEnabledPlugins, PublishPluginOperationNotification);
        }

        private static IPluginUserInteractionService GetActiveChatUserInteraction()
        {
            if (_chat == null || !_chat.TryGetActiveEditorInteraction(out IPluginUserInteractionService service) || service == null)
                return new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance).CreateService(new PluginManifest(new PluginId("alacrity.unavailable"), "Unavailable", new Version(1, 0), "Alacrity", "Unavailable", new[] { "1.4.5.6" }));
            return service;
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

        private static void RestoreEnabledPlugins()
        {
            _enabledStateStore?.RestoreOnce(_runtime, EnablePlugin, message => _notifications?.Publish(message, TimeSpan.FromSeconds(4)));
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
            if (_pluginOperations == null)
            {
                error = "Plugin runtime is unavailable.";
                return false;
            }
            return _pluginOperations.Begin(id, enable, out error);
        }

        private static bool CompletePluginOperations()
        {
            return _pluginOperations != null && _pluginOperations.CompleteFinished();
        }

        private static void PublishPluginOperationNotification(string message, TimeSpan duration)
        {
            _notifications?.Publish(message, duration);
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
            _enabledStateStore?.Persist(_runtime, message => _notifications?.Publish(message, TimeSpan.FromSeconds(4)));
        }







        private sealed class BridgePluginLogger : IPluginLogger
        {
            private readonly PluginId plugin;
            public BridgePluginLogger(PluginId plugin) { this.plugin = plugin; }
            public void Debug(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Info(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Warn(string message) { System.Diagnostics.Trace.TraceWarning("[Alacrity:" + plugin + "] " + message); }
            public void Error(string message, Exception exception = null)
            {
                System.Diagnostics.Trace.TraceError("[Alacrity:" + plugin + "] " + message + (exception == null ? string.Empty : " " + exception));
            }
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
