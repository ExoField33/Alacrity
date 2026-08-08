using System;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Hosts one internal feature with a dedicated child scope.</summary>
public sealed class PluginFeatureController
{
    private readonly IPluginFeature feature;
    private readonly PluginManifest plugin;
    private readonly IPluginResourceScope parentResources;
    private readonly IPluginLogger logger;
    private IPluginResourceScope? resources;
    public PluginFeatureController(IPluginFeature feature, PluginManifest plugin, IPluginResourceScope parentResources, IPluginLogger logger)
    {
        this.feature = feature ?? throw new ArgumentNullException(nameof(feature)); this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin)); this.parentResources = parentResources ?? throw new ArgumentNullException(nameof(parentResources)); this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public bool IsEnabled { get; private set; }
    /// <summary>Metadata for the hosted feature.</summary>
    public PluginFeatureMetadata Metadata => feature.Metadata;
    public async Task EnableAsync(CancellationToken cancellationToken)
    {
        if (IsEnabled) return;
        resources = parentResources.CreateChildScope("feature:" + feature.Metadata.Id.Value);
        try { await feature.EnableAsync(new FeatureContext(plugin, feature.Metadata, resources, logger), cancellationToken).ConfigureAwait(false); IsEnabled = true; }
        catch { resources.Dispose(); resources = null; throw; }
    }
    public async Task DisableAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled) return;
        try { await feature.DisableAsync(cancellationToken).ConfigureAwait(false); }
        finally { resources?.Dispose(); resources = null; IsEnabled = false; }
    }
    private sealed class FeatureContext : IPluginFeatureContext
    {
        public FeatureContext(PluginManifest plugin, PluginFeatureMetadata feature, IPluginResourceScope resources, IPluginLogger logger) { Plugin = plugin; Feature = feature; Resources = resources; Logger = logger; }
        public PluginManifest Plugin { get; } public PluginFeatureMetadata Feature { get; } public IPluginResourceScope Resources { get; } public IPluginLogger Logger { get; }
    }
}
