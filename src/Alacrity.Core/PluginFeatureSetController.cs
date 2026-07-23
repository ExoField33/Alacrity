using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Coordinates all independently toggleable features owned by one plugin instance.</summary>
public sealed class PluginFeatureSetController
{
    private readonly Dictionary<PluginFeatureId, PluginFeatureController> features;
    public PluginFeatureSetController(IEnumerable<IPluginFeature> features, PluginManifest plugin, IPluginResourceScope resources, IPluginLogger logger)
    {
        if (features == null) throw new ArgumentNullException(nameof(features));
        this.features = features.ToDictionary(feature => feature.Metadata.Id, feature => new PluginFeatureController(feature, plugin, resources, logger));
    }
    /// <summary>Enables a single feature when it is not server-policy denied.</summary>
    public Task EnableAsync(PluginFeatureId id, CancellationToken cancellationToken)
    {
        if (!features.TryGetValue(id, out var feature)) throw new KeyNotFoundException("Plugin feature is not registered: " + id.Value);
        return feature.EnableAsync(cancellationToken);
    }
    /// <summary>Disables one runtime-safe feature, returning false when whole-plugin disable is required.</summary>
    public async Task<bool> TryDisableAsync(PluginFeatureId id, CancellationToken cancellationToken)
    {
        if (!features.TryGetValue(id, out var feature)) throw new KeyNotFoundException("Plugin feature is not registered: " + id.Value);
        if (!feature.Metadata.CanDisableAtRuntime) return false;
        await feature.DisableAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
    /// <summary>Disables all enabled features in reverse registration order.</summary>
    public async Task DisableAllAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var feature in features.Values.Reverse())
        {
            if (!feature.IsEnabled) continue;
            try { await feature.DisableAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }
}
