using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Resolves required plugin dependencies before package code is loaded or activated.</summary>
public sealed class PluginDependencyResolver
{
    /// <summary>Returns a dependency-first load order or throws clear validation diagnostics.</summary>
    public IReadOnlyList<PluginManifest> Resolve(IEnumerable<PluginManifest> manifests)
    {
        if (manifests == null) throw new ArgumentNullException(nameof(manifests));
        var indexed = manifests.ToDictionary(manifest => manifest.Id);
        var result = new List<PluginManifest>();
        var states = new Dictionary<PluginId, VisitState>();
        foreach (var manifest in indexed.Values) Visit(manifest, indexed, states, result);
        return result;
    }

    private static void Visit(PluginManifest manifest, Dictionary<PluginId, PluginManifest> indexed, Dictionary<PluginId, VisitState> states, List<PluginManifest> result)
    {
        if (states.TryGetValue(manifest.Id, out var state))
        {
            if (state == VisitState.Completed) return;
            throw new InvalidOperationException("Plugin dependency cycle detected at " + manifest.Id + ".");
        }

        states.Add(manifest.Id, VisitState.Visiting);
        foreach (var dependency in manifest.Dependencies)
        {
            if (!indexed.TryGetValue(dependency.Id, out var provider))
                throw new InvalidOperationException("Plugin " + manifest.Id + " requires missing dependency " + dependency.Id + ".");
            if (dependency.MinimumVersion != null && provider.Version.CompareTo(dependency.MinimumVersion) < 0)
                throw new InvalidOperationException("Plugin " + manifest.Id + " requires " + dependency.Id + " version " + dependency.MinimumVersion + " or later.");
            Visit(provider, indexed, states, result);
        }
        states[manifest.Id] = VisitState.Completed;
        result.Add(manifest);
    }

    private enum VisitState { Visiting, Completed }
}
