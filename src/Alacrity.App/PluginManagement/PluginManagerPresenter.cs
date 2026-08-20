using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace Alacrity.App.PluginManagement;

/// <summary>Application-facing projection of Core plugin state; rendering/layout remains outside Core.</summary>
public sealed class PluginManagerPresenter
{
    /// <summary>Creates display rows directly from the authoritative package runtime registry.</summary>
    public IReadOnlyList<PluginManagerRow> Present(PluginManagerRuntime runtime, IReadOnlyList<PluginDependencyWarning> warnings)
    {
        if (runtime == null) throw new ArgumentNullException(nameof(runtime));
        if (warnings == null) throw new ArgumentNullException(nameof(warnings));
        return runtime.Registry.Records
            .Where(record => record.State != PluginPackageLifecycleState.Uninstalled)
            .OrderBy(record => record.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Manifest.Id.Value, StringComparer.Ordinal)
            .Select(record => new PluginManagerRow(
                record.Manifest.Id,
                record.Manifest.Name,
                record.Manifest.Publisher,
                record.Manifest.Version,
                record.Manifest.Description,
                record.Manifest.Changelog,
                ToLifecycleState(record.State),
                record.Manifest.Capabilities != PluginCapability.None,
                warnings.FirstOrDefault(warning => warning.Plugin == record.Manifest.Id)?.Reason))
            .ToArray();
    }

    /// <summary>Creates display rows from the current Core compatibility menu.</summary>
    public IReadOnlyList<PluginManagerRow> Present(PluginManagementMenu menu, IReadOnlyList<PluginDependencyWarning> warnings)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        if (warnings == null) throw new ArgumentNullException(nameof(warnings));
        return menu.SettingsEntries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id.Value, StringComparer.Ordinal)
            .Select(entry => new PluginManagerRow(entry.Id, entry.Name, entry.Author, entry.Version, entry.Description, entry.Changelog, entry.State, entry.CanConfigure, warnings.FirstOrDefault(warning => warning.Plugin == entry.Id)?.Reason))
            .ToArray();
    }

    private static PluginLifecycleState ToLifecycleState(PluginPackageLifecycleState state)
    {
        return state == PluginPackageLifecycleState.Enabled ? PluginLifecycleState.Enabled :
            state == PluginPackageLifecycleState.Loaded || state == PluginPackageLifecycleState.Disabled ? PluginLifecycleState.Disabled :
            PluginLifecycleState.Faulted;
    }
}

/// <summary>Application display data for one plugin manager row.</summary>
public sealed class PluginManagerRow
{
    internal PluginManagerRow(PluginId id, string name, string author, Version version, string description, string changelog, PluginLifecycleState state, bool canConfigure, string? warning)
    {
        Id = id; Name = name; Author = author; Version = version; Description = description; Changelog = changelog; State = state; CanConfigure = canConfigure; Warning = warning;
    }
    public PluginId Id { get; }
    public string Name { get; }
    public string Author { get; }
    public Version Version { get; }
    public string Description { get; }
    public string Changelog { get; }
    public PluginLifecycleState State { get; }
    public bool CanConfigure { get; }
    public string? Warning { get; }
    /// <summary>Whether the package is in a lifecycle state the manager can toggle.</summary>
    public bool CanToggle => State == PluginLifecycleState.Disabled || State == PluginLifecycleState.Enabled;
    /// <summary>Whether the package is active.</summary>
    public bool IsEnabled => State == PluginLifecycleState.Enabled;
}
