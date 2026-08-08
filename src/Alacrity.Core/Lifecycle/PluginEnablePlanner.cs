using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Plans dependency-first enablement and emits transient host notifications for auto-enabled packages.</summary>
public sealed class PluginEnablePlanner
{
    /// <summary>Builds an enable plan without executing plugin code.</summary>
    public PluginEnablePlan Plan(PluginId requestedPlugin, IReadOnlyList<PluginManifest> installed, IReadOnlyCollection<PluginId> currentlyEnabled)
    {
        if (installed == null) throw new ArgumentNullException(nameof(installed));
        if (currentlyEnabled == null) throw new ArgumentNullException(nameof(currentlyEnabled));
        var indexed = installed.ToDictionary(manifest => manifest.Id);
        if (!indexed.TryGetValue(requestedPlugin, out var requested))
            throw new InvalidOperationException("Requested plugin is not installed: " + requestedPlugin + ".");

        var enabled = new HashSet<PluginId>(currentlyEnabled);
        var ordered = new List<PluginId>();
        var notifications = new List<PluginEnableNotification>();
        var visiting = new HashSet<PluginId>();
        Visit(requested, requested.Id, indexed, enabled, visiting, ordered, notifications);
        return new PluginEnablePlan(ordered, notifications);
    }

    private static void Visit(PluginManifest manifest, PluginId requestedId, Dictionary<PluginId, PluginManifest> indexed, HashSet<PluginId> enabled, HashSet<PluginId> visiting, List<PluginId> ordered, List<PluginEnableNotification> notifications)
    {
        if (enabled.Contains(manifest.Id)) return;
        if (!visiting.Add(manifest.Id)) throw new InvalidOperationException("Plugin dependency cycle detected at " + manifest.Id + ".");
        foreach (var dependency in manifest.Dependencies)
        {
            if (!indexed.TryGetValue(dependency.Id, out var provider)) throw new InvalidOperationException("Plugin " + manifest.Id + " requires missing dependency " + dependency.Id + ".");
            if (dependency.MinimumVersion != null && provider.Version.CompareTo(dependency.MinimumVersion) < 0) throw new InvalidOperationException("Plugin " + manifest.Id + " requires " + dependency.Id + " version " + dependency.MinimumVersion + " or later.");
            Visit(provider, requestedId, indexed, enabled, visiting, ordered, notifications);
        }
        visiting.Remove(manifest.Id);
        enabled.Add(manifest.Id);
        ordered.Add(manifest.Id);
        if (manifest.Id != requestedId) notifications.Add(new PluginEnableNotification(manifest.Id, requestedId));
    }
}

/// <summary>Dependency-first packages the host should enable atomically.</summary>
public sealed class PluginEnablePlan
{
    internal PluginEnablePlan(IReadOnlyList<PluginId> orderedPlugins, IReadOnlyList<PluginEnableNotification> notifications) { OrderedPlugins = orderedPlugins; Notifications = notifications; }
    public IReadOnlyList<PluginId> OrderedPlugins { get; }
    public IReadOnlyList<PluginEnableNotification> Notifications { get; }
}

/// <summary>Transient notice that a dependency was enabled for a requested plugin.</summary>
public sealed class PluginEnableNotification
{
    internal PluginEnableNotification(PluginId dependency, PluginId requestedPlugin) { Dependency = dependency; RequestedPlugin = requestedPlugin; }
    public PluginId Dependency { get; }
    public PluginId RequestedPlugin { get; }
    public string Message => "Enabled dependency " + Dependency + " for " + RequestedPlugin + ".";
}
