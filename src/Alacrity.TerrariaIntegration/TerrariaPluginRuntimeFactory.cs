using System;
using System.IO;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria;

/// <summary>
/// Creates the process-wide plugin runtime and its shared Terraria adapters.
/// Plugin-facing wrappers are still created per activation by <see cref="PluginHostContextFactory"/>.
/// </summary>
internal sealed class TerrariaPluginRuntimeServices
{
    internal PluginManagerRuntime Runtime { get; private set; }
    internal PluginManagementMenu Menu { get; private set; }
    internal PluginNotificationCenter Notifications { get; private set; }
    internal PluginDependencyDiagnostics Diagnostics { get; private set; }
    internal PluginExtensionHost Extensions { get; private set; }
    internal PluginServiceHub ServiceHub { get; private set; }
    internal PluginCommandHost Commands { get; private set; }
    internal PluginOverlayHost Overlays { get; private set; }
    internal PluginHudHost Hud { get; private set; }
    internal TerrariaHudAdapter HudAdapter { get; private set; }
    internal PluginDispatcherHost Dispatcher { get; private set; }
    internal TerrariaEntitySnapshotCache EntitySnapshots { get; private set; }
    internal TerrariaSessionPresentationService SessionPresentation { get; private set; }
    internal PluginChatHost Chat { get; private set; }
    internal PluginUserInteractionHost UserInteraction { get; private set; }
    internal PluginVisualEffectsHost VisualEffects { get; private set; }

    internal static TerrariaPluginRuntimeServices Create(string root)
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
        var entitySnapshots = new TerrariaEntitySnapshotCache();
        var userInteraction = new PluginUserInteractionHost(new TerrariaPluginUserInteractionBackend());
        var sessionPresentation = new TerrariaSessionPresentationService();
        var visualEffects = new PluginVisualEffectsHost();
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
                sessionPresentation.CreateService(manifest, resources)),
            dispatcher,
            null,
            hud);
        var runtimeHost = new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), contexts);
        var activation = new PluginActivationCoordinator(
            PatchHost.CreateManaged(root, Path.Combine(patchDirectory, "journal.json")),
            new PluginEnablePlanner(),
            new PluginEnableExecutor(notifications),
            new PluginActivationGate(diagnostics));
        var runtime = new PluginManagerRuntime(runtimeHost, new PluginPackageLifecycleRegistry(), activation);

        return new TerrariaPluginRuntimeServices
        {
            Runtime = runtime,
            Menu = new PluginManagementMenu(runtime),
            Notifications = notifications,
            Diagnostics = diagnostics,
            Extensions = extensions,
            ServiceHub = serviceHub,
            Commands = commands,
            Overlays = overlays,
            Hud = hud,
            HudAdapter = new TerrariaHudAdapter(hud),
            Dispatcher = dispatcher,
            EntitySnapshots = entitySnapshots,
            SessionPresentation = sessionPresentation,
            Chat = chat,
            UserInteraction = userInteraction,
            VisualEffects = visualEffects
        };
    }
}
