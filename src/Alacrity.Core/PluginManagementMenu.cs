using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-neutral model for the Alacrity plugin entry and settings screen.</summary>
public sealed class PluginManagementMenu
{
    private readonly Dictionary<PluginId, PluginLifecycleController> controllers;
    private readonly PluginManagerRuntime? runtime;

    public PluginManagementMenu(IEnumerable<PluginLifecycleController> controllers)
    {
        if (controllers == null)
            throw new ArgumentNullException(nameof(controllers));

        this.controllers = controllers.ToDictionary(controller => controller.Manifest.Id);
        MainMenuEntries = CreateMainMenuEntries();
    }

    /// <summary>Compatibility presentation adapter over the authoritative package runtime registry.</summary>
    public PluginManagementMenu(PluginManagerRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        controllers = new Dictionary<PluginId, PluginLifecycleController>();
        MainMenuEntries = CreateMainMenuEntries();
    }

    /// <summary>Main-menu order with Plugins in the former Workshop slot.</summary>
    public IReadOnlyList<MainMenuEntry> MainMenuEntries { get; }

    /// <summary>Returns the current Workshop-style plugin rows.</summary>
    public IReadOnlyList<PluginSettingsEntry> SettingsEntries => runtime == null ? controllers.Values
        .OrderBy(controller => controller.Manifest.Name, StringComparer.OrdinalIgnoreCase)
        .Select(controller => new PluginSettingsEntry(
            controller.Manifest.Id,
            controller.Manifest.Name,
            controller.Manifest.Publisher,
            controller.Manifest.Version,
            controller.Manifest.Description,
            controller.Manifest.Changelog,
            controller.State,
            controller.Manifest.Capabilities != PluginCapability.None))
        .ToArray() : runtime.Registry.Records.Where(record => record.State != PluginPackageLifecycleState.Uninstalled).Select(record => new PluginSettingsEntry(record.Manifest.Id, record.Manifest.Name, record.Manifest.Publisher, record.Manifest.Version, record.Manifest.Description, record.Manifest.Changelog, ToPluginState(record.State), record.Manifest.Capabilities != PluginCapability.None)).ToArray();

    /// <summary>Toggles an initialized plugin and returns its new state.</summary>
    public PluginLifecycleState Toggle(PluginId id)
    {
        if (runtime != null)
        {
            var record = runtime.Registry.Records.Single(record => record.Manifest.Id == id);
            if (record.State == PluginPackageLifecycleState.Enabled) runtime.Disable(id); else runtime.Enable(id);
            return ToPluginState(runtime.Registry.Records.Single(candidate => candidate.Manifest.Id == id).State);
        }
        var controller = GetController(id);
        if (controller.State == PluginLifecycleState.Enabled)
            controller.Disable();
        else if (controller.State == PluginLifecycleState.Disabled)
            controller.Enable();
        else
            throw new InvalidOperationException("Plugin cannot be toggled from state " + controller.State + ".");
        return controller.State;
    }

    private static PluginLifecycleState ToPluginState(PluginPackageLifecycleState state)
    {
        return state == PluginPackageLifecycleState.Enabled ? PluginLifecycleState.Enabled : state == PluginPackageLifecycleState.Disabled || state == PluginPackageLifecycleState.Loaded ? PluginLifecycleState.Disabled : PluginLifecycleState.Faulted;
    }

    private PluginLifecycleController GetController(PluginId id)
    {
        if (!controllers.TryGetValue(id, out var controller))
            throw new KeyNotFoundException("Plugin is not registered: " + id);
        return controller;
    }

    private static IReadOnlyList<MainMenuEntry> CreateMainMenuEntries()
    {
        return new[]
        {
            new MainMenuEntry(MainMenuEntryId.SinglePlayer, "Single Player", 0),
            new MainMenuEntry(MainMenuEntryId.Multiplayer, "Multiplayer", 1),
            new MainMenuEntry(MainMenuEntryId.Achievements, "Achievements", 2),
            new MainMenuEntry(MainMenuEntryId.Plugins, "Plugins", 3),
            new MainMenuEntry(MainMenuEntryId.Workshop, "Workshop", 4),
            new MainMenuEntry(MainMenuEntryId.Settings, "Settings", 5),
            new MainMenuEntry(MainMenuEntryId.Credits, "Credits", 6),
            new MainMenuEntry(MainMenuEntryId.Exit, "Exit", 7)
        };
    }
}
