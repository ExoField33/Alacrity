using System.Collections.Generic;
using System.Reflection;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
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
        private static GameState.World.TerrariaWorldSectionSnapshotCache _worldSections => runtimeState == null ? null : runtimeState.WorldSections;
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

        private static string _ingameHoveredPluginActionId
        {
            get => BridgeState.IngameHoveredPluginActionId;
            set => BridgeState.IngameHoveredPluginActionId = value;
        }
        private static TerrariaPluginEnabledStateStore _enabledStateStore => runtimeState == null ? null : runtimeState.EnabledStateStore;
        private static TerrariaVisualEffectsAdapter _visualEffects => runtimeState == null ? null : runtimeState.VisualEffects;
        private static Rendering.Culling.TerrariaRenderCullingAdapter _renderCulling => runtimeState == null ? null : runtimeState.RenderCulling;
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

        /// <summary>Queues native Terraria hover text with a barely blue color-coded span.</summary>
        internal static void ShowHoverText(string text)
        {
            if (string.IsNullOrEmpty(text) || Main.instance == null)
            {
                return;
            }

            // MouseText ultimately uses ChatManager, so the normal Terraria color tag keeps the
            // tooltip in the native queue while allowing Alacrity controls a near-white blue tint.
            Main.instance.MouseText("[c/eef2ff:" + text + "]");
        }
    }
}
