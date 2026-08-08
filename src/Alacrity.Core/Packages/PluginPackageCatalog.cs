using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Discovers package manifests without loading plugin assemblies.</summary>
public sealed class PluginPackageCatalog
{
    private readonly PluginPackageManifestReader reader;
    public PluginPackageCatalog(PluginPackageManifestReader reader) { this.reader = reader ?? throw new ArgumentNullException(nameof(reader)); }

    /// <summary>Reads valid package manifests beneath the immutable package root.</summary>
    public IReadOnlyList<PluginPackageDescriptor> Discover(string alacrityRoot)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot)) throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));
        var packagesRoot = Path.Combine(Path.GetFullPath(alacrityRoot), "plugins");
        if (!Directory.Exists(packagesRoot)) return Array.Empty<PluginPackageDescriptor>();
        var packages = new List<PluginPackageDescriptor>();
        foreach (var directory in Directory.GetDirectories(packagesRoot))
        {
            var manifestPath = Path.Combine(directory, "plugin.json");
            if (!File.Exists(manifestPath)) continue;
            var manifest = reader.ReadFromFile(manifestPath);
            packages.Add(new PluginPackageDescriptor(directory, manifest));
        }
        return packages;
    }
}

/// <summary>Verified package location and its host-created manifest, before assembly loading.</summary>
public sealed class PluginPackageDescriptor
{
    internal PluginPackageDescriptor(string packageDirectory, PluginManifest manifest) { PackageDirectory = packageDirectory; Manifest = manifest; }
    public string PackageDirectory { get; }
    public PluginManifest Manifest { get; }
}

/// <summary>Prevents activation of enabled packages with unresolved dependency diagnostics.</summary>
public sealed class PluginActivationGate
{
    private readonly PluginDependencyDiagnostics diagnostics;
    public PluginActivationGate(PluginDependencyDiagnostics diagnostics) { this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)); }
    /// <summary>Returns whether activation may proceed after refreshing transient dependency warnings.</summary>
    public bool CanActivate(PluginManifest manifest, IReadOnlyList<PluginManifest> installed, IReadOnlyCollection<PluginId> enabled)
    {
        diagnostics.Refresh(installed, enabled);
        return !diagnostics.ActiveWarnings.Any(warning => warning.Plugin == manifest.Id);
    }
}
