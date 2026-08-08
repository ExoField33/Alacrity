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
using Alacrity.App.PluginManagement;
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
        /// <summary>Exact bridge ABI handshake consumed by the injected runtime before plugin bootstrap.</summary>
        public static string GetBridgeHandshake() => AlacrityCompatibility.PluginSdk + "|" + AlacrityCompatibility.Host + "|" + AlacrityCompatibility.BridgeAbi + "|1.4.5.6";
        private static PluginManagerRuntime _runtime;
        private static PluginManagementMenu _menu;
        private static PluginNotificationCenter _notifications;
        private static PluginDependencyDiagnostics _diagnostics;
        private static PluginExtensionHost _extensions;
        private static PluginServiceHub _serviceHub;
        private static PluginCommandHost _commands;
        private static TerrariaPluginDrawAdapter _drawAdapter;
        private static PluginDispatcherHost _dispatcher;
        private static PluginSchedulerHost _scheduler;
        private static readonly ClientPresentationStateTracker _presentationStates = new ClientPresentationStateTracker();
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
        private static TerrariaVisualEffectsAdapter _visualEffects;
        private static bool _runtimeBootstrapped;
        private static bool _runtimeShuttingDown;
        private static TerrariaPluginOperationCoordinator _pluginOperations;
        private static TerrariaKeybindRuntime _keybindRuntime;
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
                _extensions?.Publish(new ClientStartedEvent(TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)));
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
                _extensions?.Publish(new ClientShuttingDownEvent(TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)));
                _pluginOperations?.CancelAllAndWait(TimeSpan.FromSeconds(6));
                if (_runtime != null)
                {
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    foreach (var record in GetShutdownOrder())
                    {
                        if (_pluginOperations != null && _pluginOperations.IsPending(record.Manifest.Id))
                        {
                            ReportOptionalUiFailure("Plugin shutdown: " + record.Manifest.Id, new TimeoutException("A lifecycle operation did not stop before the shutdown timeout."));
                            continue;
                        }
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
                _ingameBlankTexture?.Dispose();
                _ingameBlankTexture = null;
                _ingameBlankTextureDevice = null;
            }
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
            _extensions?.Publish(new WorldOverlayRenderingEvent(CurrentPresentationTime));
        }

        /// <summary>Dispatches screen-space gameplay HUD overlays through Terraria's established UI SpriteBatch.</summary>
        public static void DrawHudOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawHudOverlays(spriteBatch);
            _extensions?.Publish(new HudRenderingEvent(CurrentPresentationTime));
        }

        /// <summary>Dispatches menu-space overlays from Terraria's menu SpriteBatch after version text is drawn.</summary>
        public static void DrawMenuOverlays(SpriteBatch spriteBatch)
        {
            _drawAdapter?.DrawMenuOverlays(spriteBatch);
            _extensions?.Publish(new MenuRenderingEvent(CurrentPresentationTime));
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
            if (Volatile.Read(ref _runtimeBootstrapped) && controls != null) _keybindRuntime?.AppendControls(controls);
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
                _scheduler?.Tick(Main.GameUpdateCount);
                RefreshVisualEffectsPolicy();
                TimeSpan timestamp = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                _presentationStates.Update(Main.gameMenu, Main.drawingPlayerChat, out bool menuChanged, out bool chatInputChanged);
                if (menuChanged) _extensions.Publish(new ClientMenuStateChangedEvent(Main.gameMenu, Main.GameUpdateCount, timestamp));
                if (chatInputChanged) _extensions.Publish(new ChatInputStateChangedEvent(Main.drawingPlayerChat, Main.GameUpdateCount, timestamp));
                _extensions.Publish(new ClientUpdatedEvent(Main.GameUpdateCount, timestamp));
                if (Main.gameMenu || Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.blockInput)
                    return;
                _keybindRuntime?.Dispatch();

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
            if (!Volatile.Read(ref _runtimeBootstrapped) || _keybindRuntime == null)
                return;

            _keybindRuntime.EnsureNativeStateShape();
        }

        /// <summary>Whole-system Dust fast path used before Terraria enters the DrawDust or UpdateDust loops.</summary>
        public static bool ShouldRunDustSystem() => _visualEffects == null || _visualEffects.ShouldRunDustSystem;

        /// <summary>Creation gate for Dust.NewDust. Exceptions remain live when ordinary Dust is disabled.</summary>
        public static bool ShouldCreateDust(int dustType) => _visualEffects == null || _visualEffects.ShouldCreateDust(dustType);

        /// <summary>Per-instance Dust update gate used only when exceptions require the Dust loop to run.</summary>
        public static bool ShouldUpdateDustInstance(Dust dust) => _visualEffects == null || _visualEffects.ShouldUpdateDustInstance(dust);

        /// <summary>Per-instance Dust draw gate used only when exceptions require DrawDust to run.</summary>
        public static bool ShouldDrawDustInstance(Dust dust) => _visualEffects == null || _visualEffects.ShouldUpdateDustInstance(dust);

        /// <summary>Whole-system Gore gate. Gore has no exception path.</summary>
        public static bool ShouldRunGoreSystem() => _visualEffects == null || _visualEffects.ShouldRunGoreSystem;

        private static void RefreshVisualEffectsPolicy()
        {
            _visualEffects?.Refresh();
        }

        private static TimeSpan CurrentPresentationTime => TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

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
