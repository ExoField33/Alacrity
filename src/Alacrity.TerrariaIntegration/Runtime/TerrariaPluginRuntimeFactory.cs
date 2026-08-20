using System;
using System.IO;
using Alacrity.Core;
using Alacrity.PluginSdk;
using AlacrityTerraria.Rendering.Culling;

namespace AlacrityTerraria.Runtime;

/// <summary>
/// Internal composition boundary for the Terraria client. The patch-facing bridge consumes these
/// cohesive groups, while plugin code continues to receive only <see cref="IPluginContext"/>.
/// </summary>
internal interface ITerrariaClientRuntime
{
    TerrariaLifecycleRuntime Lifecycle { get; }
    TerrariaGameStateRuntime GameState { get; }
    TerrariaRenderingRuntime Rendering { get; }
    TerrariaCommunicationRuntime Communication { get; }
    TerrariaPluginUiRuntime PluginUi { get; }
    TerrariaVisualEffectsRuntime VisualEffects { get; }
    TerrariaRenderCullingRuntime RenderCulling { get; }
    TerrariaRenderingOptimizationRuntime RenderingOptimizations { get; }
    TerrariaPresentationRuntime Presentation { get; }
}

/// <summary>Concrete, explicit Terraria runtime composition. It is created once per process.</summary>
internal sealed class TerrariaClientRuntime : ITerrariaClientRuntime
{
    internal TerrariaLifecycleRuntime Lifecycle { get; private set; }
    internal TerrariaGameStateRuntime GameState { get; private set; }
    internal TerrariaRenderingRuntime Rendering { get; private set; }
    internal TerrariaCommunicationRuntime Communication { get; private set; }
    internal TerrariaPluginUiRuntime PluginUi { get; private set; }
    internal TerrariaVisualEffectsRuntime VisualEffects { get; private set; }
    internal TerrariaRenderCullingRuntime RenderCulling { get; private set; }
    internal TerrariaRenderingOptimizationRuntime RenderingOptimizations { get; private set; }
    internal TerrariaPresentationRuntime Presentation { get; private set; }
    TerrariaLifecycleRuntime ITerrariaClientRuntime.Lifecycle => Lifecycle;
    TerrariaGameStateRuntime ITerrariaClientRuntime.GameState => GameState;
    TerrariaRenderingRuntime ITerrariaClientRuntime.Rendering => Rendering;
    TerrariaCommunicationRuntime ITerrariaClientRuntime.Communication => Communication;
    TerrariaPluginUiRuntime ITerrariaClientRuntime.PluginUi => PluginUi;
    TerrariaVisualEffectsRuntime ITerrariaClientRuntime.VisualEffects => VisualEffects;
    TerrariaRenderCullingRuntime ITerrariaClientRuntime.RenderCulling => RenderCulling;
    TerrariaRenderingOptimizationRuntime ITerrariaClientRuntime.RenderingOptimizations => RenderingOptimizations;
    TerrariaPresentationRuntime ITerrariaClientRuntime.Presentation => Presentation;

    internal static ITerrariaClientRuntime Create(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A runtime root is required.", nameof(root));

        string patchDirectory = Path.Combine(root, "data", "patches");
        Directory.CreateDirectory(patchDirectory);

        var notifications = new PluginNotificationCenter();
        var diagnostics = new PluginDependencyDiagnostics();
        var extensions = new PluginExtensionHost();
        var serviceHub = new PluginServiceHub();
        var chat = new PluginChatHost();
        var commands = new PluginCommandHost();
        var overlays = new PluginOverlayHost();
        var hud = new PluginHudHost();
        var dispatcher = new PluginDispatcherHost();
        var scheduler = new PluginSchedulerHost();
        var entitySnapshots = new TerrariaEntitySnapshotCache();
        var worldSections = new TerrariaWorldSectionSnapshotCache();
        var userInteraction = new PluginUserInteractionHost(new TerrariaPluginUserInteractionBackend());
        var sessionPresentation = new TerrariaSessionPresentationService();
        var visualEffects = new PluginVisualEffectsHost();
        var renderCulling = new PluginRenderCullingHost();
        var renderingOptimizations = new PluginRenderingOptimizationHost();
        var presentation = new PluginPresentationSuppressionHost();
        chat.MessagePresentationUpdated += TerrariaChatRuntime.QueuePresentationRefresh;
        var contexts = new PluginHostContextFactory(
            root,
            serviceHub,
            extensions,
            commands,
            overlays,
            chat,
            userInteraction,
            notifications,
            (manifest, resources, chatService) => new PluginTerrariaServices(
                chatService,
                entitySnapshots.CreateService(manifest, resources),
                visualEffects.CreateService(manifest, resources),
                entitySnapshots.CreatePlayerService(manifest, resources),
                sessionPresentation.CreateService(manifest, resources),
                entitySnapshots.CreateNpcTargetService(manifest, resources),
                TerrariaWorldSectionService.CreateService(worldSections, manifest, resources),
                renderCulling.CreateService(manifest, resources),
                renderingOptimizations.CreateService(manifest, resources),
                presentation.CreateService(manifest, resources)),
            dispatcher,
            null,
            hud,
            scheduler);
        var runtimeHost = new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), contexts);
        var activation = new PluginActivationCoordinator(
            PatchHost.CreateManaged(root, Path.Combine(patchDirectory, "journal.json")),
            new PluginEnablePlanner(),
            new PluginEnableExecutor(notifications),
            new PluginActivationGate(diagnostics));
        var runtime = new PluginManagerRuntime(runtimeHost, new PluginPackageLifecycleRegistry(), activation);

        return new TerrariaClientRuntime
        {
            Lifecycle = new TerrariaLifecycleRuntime(runtime, diagnostics, dispatcher, scheduler),
            GameState = new TerrariaGameStateRuntime(entitySnapshots, sessionPresentation, worldSections),
            Rendering = new TerrariaRenderingRuntime(notifications, overlays, hud, new TerrariaHudAdapter(hud)),
            Communication = new TerrariaCommunicationRuntime(commands, chat, userInteraction),
            PluginUi = new TerrariaPluginUiRuntime(new PluginManagementMenu(runtime), extensions, serviceHub),
            VisualEffects = new TerrariaVisualEffectsRuntime(visualEffects),
            RenderCulling = new TerrariaRenderCullingRuntime(renderCulling),
            RenderingOptimizations = new TerrariaRenderingOptimizationRuntime(renderingOptimizations),
            Presentation = new TerrariaPresentationRuntime(presentation)
        };
    }
}

/// <summary>Package activation, dependency diagnostics, and update scheduling.</summary>
internal sealed class TerrariaLifecycleRuntime
{
    internal TerrariaLifecycleRuntime(PluginManagerRuntime pluginRuntime, PluginDependencyDiagnostics diagnostics, PluginDispatcherHost dispatcher, PluginSchedulerHost scheduler)
    { PluginRuntime = pluginRuntime; Diagnostics = diagnostics; Dispatcher = dispatcher; Scheduler = scheduler; }
    internal PluginManagerRuntime PluginRuntime { get; }
    internal PluginDependencyDiagnostics Diagnostics { get; }
    internal PluginDispatcherHost Dispatcher { get; }
    internal PluginSchedulerHost Scheduler { get; }
}

/// <summary>Shared detached game-state capture adapters.</summary>
internal sealed class TerrariaGameStateRuntime
{
    internal TerrariaGameStateRuntime(TerrariaEntitySnapshotCache entities, TerrariaSessionPresentationService session, TerrariaWorldSectionSnapshotCache worldSections) { Entities = entities; Session = session; WorldSections = worldSections; }
    internal TerrariaEntitySnapshotCache Entities { get; }
    internal TerrariaSessionPresentationService Session { get; }
    internal TerrariaWorldSectionSnapshotCache WorldSections { get; }
}

/// <summary>Host-owned HUD, overlay, and notification rendering services.</summary>
internal sealed class TerrariaRenderingRuntime
{
    internal TerrariaRenderingRuntime(PluginNotificationCenter notifications, PluginOverlayHost overlays, PluginHudHost hud, TerrariaHudAdapter hudAdapter) { Notifications = notifications; Overlays = overlays; Hud = hud; HudAdapter = hudAdapter; }
    internal PluginNotificationCenter Notifications { get; }
    internal PluginOverlayHost Overlays { get; }
    internal PluginHudHost Hud { get; }
    internal TerrariaHudAdapter HudAdapter { get; }
}

/// <summary>Chat, command, and user-approved external interaction services.</summary>
internal sealed class TerrariaCommunicationRuntime
{
    internal TerrariaCommunicationRuntime(PluginCommandHost commands, PluginChatHost chat, PluginUserInteractionHost userInteraction) { Commands = commands; Chat = chat; UserInteraction = userInteraction; }
    internal PluginCommandHost Commands { get; }
    internal PluginChatHost Chat { get; }
    internal PluginUserInteractionHost UserInteraction { get; }
}

/// <summary>Plugin management menu and retained host UI registration services.</summary>
internal sealed class TerrariaPluginUiRuntime
{
    internal TerrariaPluginUiRuntime(PluginManagementMenu menu, PluginExtensionHost extensions, PluginServiceHub serviceHub) { Menu = menu; Extensions = extensions; ServiceHub = serviceHub; }
    internal PluginManagementMenu Menu { get; }
    internal PluginExtensionHost Extensions { get; }
    internal PluginServiceHub ServiceHub { get; }
}

/// <summary>Reusable local visual-presentation policy service.</summary>
internal sealed class TerrariaVisualEffectsRuntime
{
    internal TerrariaVisualEffectsRuntime(PluginVisualEffectsHost policies) { Policies = policies; }
    internal PluginVisualEffectsHost Policies { get; }
}

/// <summary>Reusable conservative world-render culling policy service.</summary>
internal sealed class TerrariaRenderCullingRuntime
{
    internal TerrariaRenderCullingRuntime(PluginRenderCullingHost policies) { Policies = policies; }
    internal PluginRenderCullingHost Policies { get; }
}

/// <summary>Host-owned composition for optional local presentation suppression policies.</summary>
internal sealed class TerrariaPresentationRuntime
{
    internal TerrariaPresentationRuntime(PluginPresentationSuppressionHost policies)
    {
        Policies = policies;
    }

    internal PluginPresentationSuppressionHost Policies { get; }
}

/// <summary>Reusable host-owned local rendering optimization policies.</summary>
internal sealed class TerrariaRenderingOptimizationRuntime
{
    internal TerrariaRenderingOptimizationRuntime(PluginRenderingOptimizationHost policies)
    {
        Policies = policies;
    }

    internal PluginRenderingOptimizationHost Policies { get; }
}
