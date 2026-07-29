using System;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Concrete host context assembled from verified package metadata and scope-owned services.</summary>
public sealed class PluginHostContext : IPluginContext
{
    internal PluginHostContext(PluginManifest manifest, IPluginLogger logger, IPluginResourceScope resources, IPluginSettings settings, IPluginStorage storage, IPluginEventService events, IPluginCommandService commands, IPluginKeybindService keybinds, IPluginUiService ui, IPluginOverlayService overlays, IPluginUserInteractionService userInteraction, ITerrariaServices terraria, IPluginServiceRegistry services, IMultiplayerSession multiplayer)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Keybinds = keybinds ?? throw new ArgumentNullException(nameof(keybinds));
        Ui = ui ?? throw new ArgumentNullException(nameof(ui));
        Overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
        UserInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction));
        Terraria = terraria ?? throw new ArgumentNullException(nameof(terraria));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Multiplayer = multiplayer ?? throw new ArgumentNullException(nameof(multiplayer));
    }
    public PluginManifest Manifest { get; }
    public IPluginLogger Logger { get; }
    public IPluginResourceScope Resources { get; }
    public IPluginSettings Settings { get; }
    public IPluginStorage Storage { get; }
    public IPluginEventService Events { get; }
    public IPluginCommandService Commands { get; }
    public IPluginKeybindService Keybinds { get; }
    public IPluginUiService Ui { get; }
    public IPluginOverlayService Overlays { get; }
    public IPluginUserInteractionService UserInteraction { get; }
    public ITerrariaServices Terraria { get; }
    public IPluginServiceRegistry Services { get; }
    public IMultiplayerSession Multiplayer { get; }
}

/// <summary>Host factory that creates exactly one resource scope and service set for a plugin enable cycle.</summary>
public sealed class PluginHostContextFactory
{
    private readonly string alacrityRoot;
    private readonly PluginServiceHub services;
    private readonly PluginExtensionHost extensions;
    private readonly PluginCommandHost commands;
    private readonly PluginOverlayHost overlays;
    private readonly PluginChatHost chat;
    private readonly PluginUserInteractionHost userInteraction;

    public PluginHostContextFactory(string alacrityRoot, PluginServiceHub services, PluginExtensionHost extensions, PluginCommandHost commands, PluginOverlayHost? overlays = null, PluginChatHost? chat = null, PluginUserInteractionHost? userInteraction = null)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot)) throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));
        this.alacrityRoot = alacrityRoot;
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.overlays = overlays ?? new PluginOverlayHost();
        this.chat = chat ?? new PluginChatHost();
        this.userInteraction = userInteraction ?? new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance);
    }

    /// <summary>Creates a context after manifest verification and before plugin initialization.</summary>
    public PluginHostContext Create(PluginManifest manifest, IPluginLogger logger, IMultiplayerSession multiplayer)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        manifest.Validate();
        var resources = new PluginResourceScope();
        var extensionServices = extensions.CreateServices(manifest, resources);
        return new PluginHostContext(manifest, logger, resources,
            new PluginSettingsStore(alacrityRoot, manifest.Id), new PluginDataStore(alacrityRoot, manifest.Id),
            extensionServices.Events, commands.CreateService(resources), extensionServices.Keybinds,
            extensionServices.Ui, overlays.CreateService(manifest, resources), userInteraction.CreateService(manifest), new PluginTerrariaServices(chat.CreateService(manifest, resources)), services.CreateRegistry(manifest, resources), multiplayer);
    }
}
