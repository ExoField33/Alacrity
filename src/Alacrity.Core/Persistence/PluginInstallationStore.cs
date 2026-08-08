using System;
using System.IO;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned immutable package location boundary, separate from plugin user data.</summary>
public sealed class PluginInstallationStore
{
    private readonly string packagesRoot;
    public PluginInstallationStore(string alacrityRoot)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot)) throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));
        packagesRoot = Path.Combine(Path.GetFullPath(alacrityRoot), "plugins");
        Directory.CreateDirectory(packagesRoot);
    }
    /// <summary>Absolute immutable package directory for a valid plugin identifier.</summary>
    public string GetPackageDirectory(PluginId pluginId)
    {
        if (!pluginId.IsValid) throw new ArgumentException("A valid plugin ID is required.", nameof(pluginId));
        var path = Path.GetFullPath(Path.Combine(packagesRoot, pluginId.Value));
        var prefix = packagesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Plugin installation cannot escape the packages root.");
        return path;
    }
    /// <summary>Checks whether an immutable package directory is present.</summary>
    public bool Exists(PluginId pluginId) => Directory.Exists(GetPackageDirectory(pluginId));
}
