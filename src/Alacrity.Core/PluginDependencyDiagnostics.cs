using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Transient dependency warnings for enabled plugins; consumers render only active entries.</summary>
public sealed class PluginDependencyDiagnostics
{
    private readonly Dictionary<PluginId, PluginDependencyWarning> warnings = new Dictionary<PluginId, PluginDependencyWarning>();
    /// <summary>Raised whenever the active warning set changes.</summary>
    public event EventHandler? Changed;
    /// <summary>Warnings that should currently be shown by the application layer.</summary>
    public IReadOnlyList<PluginDependencyWarning> ActiveWarnings => warnings.Values.ToArray();

    /// <summary>Replaces warnings based on current enabled state and available manifests.</summary>
    public void Refresh(IEnumerable<PluginManifest> manifests, IEnumerable<PluginId> enabledPlugins)
    {
        if (manifests == null) throw new ArgumentNullException(nameof(manifests));
        if (enabledPlugins == null) throw new ArgumentNullException(nameof(enabledPlugins));
        var available = manifests.ToDictionary(manifest => manifest.Id);
        var enabled = new HashSet<PluginId>(enabledPlugins);
        var next = new Dictionary<PluginId, PluginDependencyWarning>();
        foreach (var id in enabled)
        {
            if (!available.TryGetValue(id, out var manifest)) continue;
            foreach (var dependency in manifest.Dependencies)
            {
                if (!enabled.Contains(dependency.Id))
                {
                    next[id] = new PluginDependencyWarning(id, dependency.Id, "Dependency is disabled or unavailable.");
                    break;
                }
                if (!available.TryGetValue(dependency.Id, out var provider) || (dependency.MinimumVersion != null && provider.Version.CompareTo(dependency.MinimumVersion) < 0))
                {
                    next[id] = new PluginDependencyWarning(id, dependency.Id, "Dependency version is unavailable.");
                    break;
                }
            }
        }
        if (warnings.Count == next.Count && warnings.All(pair => next.TryGetValue(pair.Key, out var warning) && warning.Equals(pair.Value))) return;
        warnings.Clear();
        foreach (var pair in next) warnings.Add(pair.Key, pair.Value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One transient warning preventing an enabled plugin from using a missing dependency.</summary>
public sealed class PluginDependencyWarning : IEquatable<PluginDependencyWarning>
{
    public PluginDependencyWarning(PluginId plugin, PluginId dependency, string reason) { Plugin = plugin; Dependency = dependency; Reason = reason; }
    public PluginId Plugin { get; }
    public PluginId Dependency { get; }
    public string Reason { get; }
    public bool Equals(PluginDependencyWarning? other) => other != null && Plugin == other.Plugin && Dependency == other.Dependency && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
    public override bool Equals(object? obj) => Equals(obj as PluginDependencyWarning);
    public override int GetHashCode() => Plugin.GetHashCode() ^ Dependency.GetHashCode() ^ Reason.GetHashCode();
}
