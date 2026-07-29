using System;
using System.IO;
using System.Reflection;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Loads the manifest-declared entry type only after package verification and activation gating.</summary>
public sealed class PluginAssemblyLoader
{
    private readonly IPluginLoadContext loadContext;

    public PluginAssemblyLoader(IPluginLoadContext? loadContext = null)
    {
        this.loadContext = loadContext ?? new NetFrameworkPluginLoadContext();
    }

    public IPluginLoadContext LoadContext => loadContext;
    /// <summary>Loads either supported lifecycle contract and rejects ambiguous entries.</summary>
    public object LoadAny(PluginPackageDescriptor package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        var manifest = package.Manifest;
        if (manifest.EntryAssembly == null || manifest.EntryType == null) throw new InvalidOperationException("plugin.json must declare entryAssembly and entryType before a plugin can load.");
        var assemblyPath = Path.GetFullPath(Path.Combine(package.PackageDirectory, manifest.EntryAssembly));
        var prefix = Path.GetFullPath(package.PackageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!assemblyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Plugin entry assembly escapes its package directory.");
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("The manifest-declared entry assembly is missing.", assemblyPath);
        var type = loadContext.Load(assemblyPath).GetType(manifest.EntryType, true);
        bool synchronous = typeof(IAlacrityPlugin).IsAssignableFrom(type);
        bool asynchronous = typeof(IAsyncAlacrityPlugin).IsAssignableFrom(type);
        if (synchronous == asynchronous) throw new InvalidOperationException(synchronous ? "The manifest-declared entry type implements both plugin lifecycle contracts." : "The manifest-declared entry type does not implement a supported plugin lifecycle contract.");
        return Activator.CreateInstance(type)!;
    }
    /// <summary>Creates the declared legacy plugin entry instance from a verified package descriptor.</summary>
    public IAlacrityPlugin Load(PluginPackageDescriptor package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        var manifest = package.Manifest;
        if (manifest.EntryAssembly == null || manifest.EntryType == null)
            throw new InvalidOperationException("plugin.json must declare entryAssembly and entryType before a plugin can load.");
        var assemblyPath = Path.GetFullPath(Path.Combine(package.PackageDirectory, manifest.EntryAssembly));
        var prefix = Path.GetFullPath(package.PackageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!assemblyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Plugin entry assembly escapes its package directory.");
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("The manifest-declared entry assembly is missing.", assemblyPath);
        object plugin = LoadAny(package);
        if (plugin is not IAlacrityPlugin synchronous) throw new InvalidOperationException("The manifest-declared entry type is asynchronous; use LoadAny through the async-aware lifecycle host.");
        return synchronous;
    }
}
