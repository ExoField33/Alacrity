using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-only package runtime path: discover metadata first, then load trusted entries into disabled controllers.</summary>
public sealed class PluginRuntimeHost
{
    private readonly PluginPackageCatalog catalog;
    private readonly PluginAssemblyLoader loader;
    private readonly PluginHostContextFactory contexts;
    private readonly string currentGameVersion;
    public PluginRuntimeHost(PluginPackageCatalog catalog, PluginAssemblyLoader loader, PluginHostContextFactory contexts, string currentGameVersion = "1.4.5.6")
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        if (string.IsNullOrWhiteSpace(currentGameVersion)) throw new ArgumentException("A current Terraria version is required.", nameof(currentGameVersion));
        this.currentGameVersion = currentGameVersion;
    }
    /// <summary>Authoritative Terraria version supplied by the host process.</summary>
    public string CurrentGameVersion => currentGameVersion;
    /// <summary>Discovers package manifests without loading plugin assemblies.</summary>
    public IReadOnlyList<PluginPackageDescriptor> Discover(string alacrityRoot) => catalog.Discover(alacrityRoot);
    /// <summary>Loads a trusted package into a validated-but-disabled lifecycle controller.</summary>
    public PluginLifecycleController LoadTrusted(PluginPackageDescriptor package, PluginTrustVerificationResult trust, IPluginLogger logger, IMultiplayerSession multiplayer)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        if (trust == null) throw new ArgumentNullException(nameof(trust));
        if (trust.Level == PluginTrustLevel.Modified || trust.Level == PluginTrustLevel.Revoked || trust.Level == PluginTrustLevel.Expired)
            throw new InvalidOperationException("Package loading is blocked by trust result: " + trust.Level + ".");
        PluginGameVersionCompatibility.EnsureSupported(package.Manifest, currentGameVersion);
        var context = contexts.Create(package.Manifest, logger, multiplayer);
        var controller = new PluginLifecycleController(loader.LoadAny(package), context);
        controller.Validate();
        return controller;
    }
}
