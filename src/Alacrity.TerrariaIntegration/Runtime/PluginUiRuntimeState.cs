using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using AlacrityTerraria.Rendering.Culling;

namespace AlacrityTerraria.Runtime;

/// <summary>
/// Process-owned managed runtime state behind the version-locked static bridge facade.
/// The facade forwards through this object; plugins never receive it directly.
/// </summary>
internal sealed class PluginUiRuntimeState
{
    private PluginUiRuntimeState(
        ITerrariaClientRuntime services,
        string root,
        Action ensureChatRuntime,
        Func<IPluginUserInteractionService> getActiveChatInteraction,
        Action<string, Exception> reportFailure,
        Action persistEnabledState,
        Action<string, TimeSpan> publishNotification)
    {
        Runtime = services.Lifecycle.PluginRuntime;
        Menu = services.PluginUi.Menu;
        Notifications = services.Rendering.Notifications;
        Diagnostics = services.Lifecycle.Diagnostics;
        Extensions = services.PluginUi.Extensions;
        ServiceHub = services.PluginUi.ServiceHub;
        Commands = services.Communication.Commands;
        Dispatcher = services.Lifecycle.Dispatcher;
        Scheduler = services.Lifecycle.Scheduler;
        EntitySnapshots = services.GameState.Entities;
        SessionPresentation = services.GameState.Session;
        Chat = services.Communication.Chat;
        UserInteraction = services.Communication.UserInteraction;
        DrawAdapter = new TerrariaPluginDrawAdapter(services.Rendering.Notifications, services.Rendering.Overlays, services.Rendering.Hud, services.Rendering.HudAdapter, services.GameState.Entities, reportFailure);
        ChatAdapter = new TerrariaPluginChatAdapter(Chat, ensureChatRuntime, getActiveChatInteraction, reportFailure);
        VisualEffects = new TerrariaVisualEffectsAdapter(services.VisualEffects.Policies, reportFailure);
        RenderCulling = new TerrariaRenderCullingAdapter(services.RenderCulling.Policies, reportFailure);
        EnabledStateStore = new TerrariaPluginEnabledStateStore(root);
        KeybindRuntime = new TerrariaKeybindRuntime(root, Extensions, Notifications, reportFailure);
        Operations = new TerrariaPluginOperationCoordinator(Runtime, persistEnabledState, publishNotification);
        BridgeState = new PluginUiRuntimeBridgeState();
    }

    internal PluginManagerRuntime Runtime { get; }
    internal PluginManagementMenu Menu { get; }
    internal PluginNotificationCenter Notifications { get; }
    internal PluginDependencyDiagnostics Diagnostics { get; }
    internal PluginExtensionHost Extensions { get; }
    internal PluginServiceHub ServiceHub { get; }
    internal PluginCommandHost Commands { get; }
    internal TerrariaPluginDrawAdapter DrawAdapter { get; }
    internal PluginDispatcherHost Dispatcher { get; }
    internal PluginSchedulerHost Scheduler { get; }
    internal TerrariaEntitySnapshotCache EntitySnapshots { get; }
    internal TerrariaSessionPresentationService SessionPresentation { get; }
    internal PluginChatHost Chat { get; }
    internal TerrariaPluginChatAdapter ChatAdapter { get; }
    internal PluginUserInteractionHost UserInteraction { get; }
    internal TerrariaVisualEffectsAdapter VisualEffects { get; }
    internal TerrariaRenderCullingAdapter RenderCulling { get; }
    internal TerrariaPluginEnabledStateStore EnabledStateStore { get; }
    internal TerrariaPluginOperationCoordinator Operations { get; }
    internal TerrariaKeybindRuntime KeybindRuntime { get; }

    /// <summary>State for presentation helpers called by the version-locked static facade.</summary>
    internal PluginUiRuntimeBridgeState BridgeState { get; }

    internal static PluginUiRuntimeState Create(
        string root,
        Action ensureChatRuntime,
        Func<IPluginUserInteractionService> getActiveChatInteraction,
        Action<string, Exception> reportFailure,
        Action persistEnabledState,
        Action<string, TimeSpan> publishNotification)
    {
        return new PluginUiRuntimeState(
            TerrariaClientRuntime.Create(root),
            root,
            ensureChatRuntime,
            getActiveChatInteraction,
            reportFailure,
            persistEnabledState,
            publishNotification);
    }
}
