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
        /// <remarks>
        /// This entry point deliberately uses only locally compiled constants and BCL string formatting.
        /// A stale PluginSdk must not make the compatibility diagnostic itself uncallable.
        /// Integration tests assert these values remain synchronized with <see cref="AlacrityCompatibility"/>.
        /// </remarks>
        public static string GetBridgeHandshake() => "2|2|2|1.4.5.6";
        private static readonly PluginUiRuntimeHost RuntimeHost = new PluginUiRuntimeHost();
        private static PluginUiRuntimeState runtimeState => RuntimeHost.State;
        private static PluginManagerRuntime _runtime => runtimeState == null ? null : runtimeState.Runtime;
        private static PluginManagementMenu _menu => runtimeState == null ? null : runtimeState.Menu;
        private static PluginNotificationCenter _notifications => runtimeState == null ? null : runtimeState.Notifications;
        private static PluginDependencyDiagnostics _diagnostics => runtimeState == null ? null : runtimeState.Diagnostics;
        private static PluginExtensionHost _extensions => runtimeState == null ? null : runtimeState.Extensions;
        private static PluginServiceHub _serviceHub => runtimeState == null ? null : runtimeState.ServiceHub;
        private static PluginCommandHost _commands => runtimeState == null ? null : runtimeState.Commands;
        private static TerrariaPluginDrawAdapter _drawAdapter => runtimeState == null ? null : runtimeState.DrawAdapter;
        private static PluginDispatcherHost _dispatcher => runtimeState == null ? null : runtimeState.Dispatcher;
        private static PluginSchedulerHost _scheduler => runtimeState == null ? null : runtimeState.Scheduler;
        // The fallback only records diagnostics before bootstrap. Normal gameplay state belongs
        // to the managed runtime instance rather than the version-locked static facade.
        private static readonly PluginUiRuntimeBridgeState FallbackBridgeState = new PluginUiRuntimeBridgeState();
        private static PluginUiRuntimeBridgeState BridgeState => runtimeState == null ? FallbackBridgeState : runtimeState.BridgeState;
        private static ClientPresentationStateTracker _presentationStates => BridgeState.PresentationStates;
        private static TerrariaEntitySnapshotCache _entitySnapshots => runtimeState == null ? null : runtimeState.EntitySnapshots;
        private static TerrariaSessionPresentationService _sessionPresentation => runtimeState == null ? null : runtimeState.SessionPresentation;
        private static PluginChatHost _chat => runtimeState == null ? null : runtimeState.Chat;
        private static TerrariaPluginChatAdapter _chatAdapter => runtimeState == null ? null : runtimeState.ChatAdapter;
        private static PluginUserInteractionHost _userInteraction => runtimeState == null ? null : runtimeState.UserInteraction;
        private static PluginManagerPresenter _presenter => BridgeState.Presenter;
        private static readonly Color ResourcePackBackground = new Color(26, 40, 89) * 0.8f;
        private static readonly Color ResourcePackBorder = new Color(13, 20, 44) * 0.8f;
        private static readonly Color ResourcePackHoverBackground = new Color(46, 60, 119);
        private static readonly Color ResourcePackHoverBorder = new Color(20, 30, 56);
        private static MethodInfo _assetRequest
        {
            get => BridgeState.AssetRequest;
            set => BridgeState.AssetRequest = value;
        }

        private static MethodInfo _assetFrame
        {
            get => BridgeState.AssetFrame;
            set => BridgeState.AssetFrame = value;
        }

        private static PropertyInfo _assetValue
        {
            get => BridgeState.AssetValue;
            set => BridgeState.AssetValue = value;
        }

        private static FieldInfo _mainAssetsField
        {
            get => BridgeState.MainAssetsField;
            set => BridgeState.MainAssetsField = value;
        }
        private static HashSet<string> ReportedOptionalUiFailures => BridgeState.ReportedOptionalUiFailures;
        private static Texture2D _ingameBlankTexture
        {
            get => BridgeState.IngameBlankTexture;
            set => BridgeState.IngameBlankTexture = value;
        }

        private static GraphicsDevice _ingameBlankTextureDevice
        {
            get => BridgeState.IngameBlankTextureDevice;
            set => BridgeState.IngameBlankTextureDevice = value;
        }

        private static bool _pluginMenuOpen
        {
            get => BridgeState.PluginMenuOpen;
            set => BridgeState.PluginMenuOpen = value;
        }

        private static PluginSelectionMenu _selectionMenu
        {
            get => BridgeState.SelectionMenu;
            set => BridgeState.SelectionMenu = value;
        }

        private static PluginManagerRow[] _ingameEntries
        {
            get => BridgeState.IngameEntries;
            set => BridgeState.IngameEntries = value;
        }

        private static int _ingameSelectedEntry
        {
            get => BridgeState.IngameSelectedEntry;
            set => BridgeState.IngameSelectedEntry = value;
        }

        private static int _ingameView
        {
            get => BridgeState.IngameView;
            set => BridgeState.IngameView = value;
        }

        private static float _ingameScroll
        {
            get => BridgeState.IngameScroll;
            set => BridgeState.IngameScroll = value;
        }

        private static float _ingameDescriptionScroll
        {
            get => BridgeState.IngameDescriptionScroll;
            set => BridgeState.IngameDescriptionScroll = value;
        }

        private static string _ingameHoveredSettingId
        {
            get => BridgeState.IngameHoveredSettingId;
            set => BridgeState.IngameHoveredSettingId = value;
        }
        private static TerrariaPluginEnabledStateStore _enabledStateStore => runtimeState == null ? null : runtimeState.EnabledStateStore;
        private static TerrariaVisualEffectsAdapter _visualEffects => runtimeState == null ? null : runtimeState.VisualEffects;
        private static TerrariaPluginOperationCoordinator _pluginOperations => runtimeState == null ? null : runtimeState.Operations;
        private static TerrariaKeybindRuntime _keybindRuntime => runtimeState == null ? null : runtimeState.KeybindRuntime;
        private static uint _iconInteractionInputTick
        {
            get => BridgeState.IconInteractionInputTick;
            set => BridgeState.IconInteractionInputTick = value;
        }

        private static bool _iconInteractionWasDown
        {
            get => BridgeState.IconInteractionWasDown;
            set => BridgeState.IconInteractionWasDown = value;
        }

        private static bool _iconInteractionPressed
        {
            get => BridgeState.IconInteractionPressed;
            set => BridgeState.IconInteractionPressed = value;
        }

        private static bool _iconInteractionConsumed
        {
            get => BridgeState.IconInteractionConsumed;
            set => BridgeState.IconInteractionConsumed = value;
        }

        /// <summary>Creates and starts the package runtime once during normal Terraria startup.</summary>
        public static void BootstrapPluginRuntime()
        {
            if (!RuntimeHost.TryBeginBootstrap())
            {
                return;
            }

            try
            {
                EnsurePluginManager();
                RefreshPluginCatalog();
                _extensions?.Publish(new ClientStartedEvent(TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)));
                RuntimeHost.CompleteBootstrap();
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin runtime bootstrap", exception);
            }
            finally
            {
                RuntimeHost.EndBootstrap();
            }
        }

        /// <summary>Best-effort process-exit cleanup. Individual plugin failures never block Terraria shutdown.</summary>
        public static void ShutdownPluginRuntime()
        {
            if (!RuntimeHost.TryBeginShutdown())
            {
                return;
            }

            // Do not hold the runtime host admission gate while publishing events, invoking lifecycle callbacks, or
            // coordinating workers: every one of those paths may execute plugin-controlled code.
            _extensions?.Publish(new ClientShuttingDownEvent(TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)));
            _scheduler?.StopAcceptingWork();
            if (_pluginOperations != null)
            {
                ObserveShutdownTask(
                    "Plugin lifecycle operation shutdown",
                    _pluginOperations.CancelAllAsync(TimeSpan.FromSeconds(6)));
            }
            if (_runtime != null)
            {
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
                            BeginAsyncControllerShutdown(record.Manifest.Id, record.Controller);
                        else
                            record.Controller?.Dispose();
                    }
                    catch (Exception exception) { ReportOptionalUiFailure("Plugin shutdown: " + record.Manifest.Id, exception); }
                }
            }
            if (_scheduler != null)
            {
                ObserveShutdownTask(
                    "Plugin background shutdown",
                    _scheduler.CancelAndDrainBackgroundWorkAsync(TimeSpan.FromSeconds(3)));
            }
            _drawAdapter?.Dispose();
            _ingameBlankTexture?.Dispose();
            _ingameBlankTexture = null;
            _ingameBlankTextureDevice = null;
        }

        private static void BeginAsyncControllerShutdown(PluginId pluginId, PluginLifecycleController controller)
        {
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            try
            {
                _ = ObserveAsyncControllerShutdown(pluginId, controller.DisposeAsync(cancellation.Token), cancellation);
            }
            catch (Exception exception)
            {
                cancellation.Dispose();
                ReportOptionalUiFailure("Plugin shutdown: " + pluginId.Value, exception);
            }
        }

        private static async Task ObserveAsyncControllerShutdown(PluginId pluginId, Task shutdown, CancellationTokenSource cancellation)
        {
            try
            {
                await shutdown.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Trace.WriteLine("Alacrity async plugin shutdown failed for " + pluginId.Value + ": " + exception);
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private static void ObserveShutdownTask(string operation, Task<bool> task)
        {
            _ = ObserveShutdownTaskAsync(operation, task);
        }

        private static async Task ObserveShutdownTaskAsync(string operation, Task<bool> task)
        {
            try
            {
                if (!await task.ConfigureAwait(false))
                {
                    Trace.WriteLine("Alacrity " + operation + " exceeded its bounded cancellation timeout.");
                }
            }
            catch (Exception exception)
            {
                Trace.WriteLine("Alacrity " + operation + " failed: " + exception);
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
            if (RuntimeHost.IsBootstrapped && controls != null) _keybindRuntime?.AppendControls(controls);
        }

        /// <summary>
        /// Polls host-owned plugin bindings at the established gameplay UI boundary. The native
        /// input profile remains the source of persisted bindings; no plugin receives raw input.
        /// </summary>
        public static void UpdatePluginKeybinds()
        {
            if (!RuntimeHost.IsBootstrapped || RuntimeHost.IsShuttingDown || _extensions == null)
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
            if (!RuntimeHost.IsBootstrapped || _keybindRuntime == null)
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
