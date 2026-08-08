using System;
using System.IO;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned uninstall operation that separates immutable package removal from optional user-data removal.</summary>
public sealed class PluginUninstallService
{
    private readonly PluginInstallationStore installations;
    private readonly string dataPluginsRoot;
    public PluginUninstallService(string alacrityRoot, PluginInstallationStore installations)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot)) throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));
        this.installations = installations ?? throw new ArgumentNullException(nameof(installations));
        dataPluginsRoot = Path.Combine(Path.GetFullPath(alacrityRoot), "data", "plugins");
    }
    /// <summary>Returns the exact paths that an uninstall will remove.</summary>
    public PluginUninstallPlan Plan(PluginId pluginId, bool removeUserData)
    {
        var package = installations.GetPackageDirectory(pluginId);
        var data = Path.Combine(dataPluginsRoot, pluginId.Value);
        return new PluginUninstallPlan(pluginId, package, removeUserData ? data : null);
    }
    /// <summary>Removes only paths enumerated by a previously created host-owned plan.</summary>
    public void Execute(PluginUninstallPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        DeleteIfPresent(plan.PackageDirectory);
        if (plan.UserDataDirectory != null) DeleteIfPresent(plan.UserDataDirectory);
    }
    private static void DeleteIfPresent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}

/// <summary>Explicit package and optional user-data paths selected for one uninstall.</summary>
public sealed class PluginUninstallPlan
{
    internal PluginUninstallPlan(PluginId pluginId, string packageDirectory, string? userDataDirectory) { PluginId = pluginId; PackageDirectory = packageDirectory; UserDataDirectory = userDataDirectory; }
    public PluginId PluginId { get; }
    public string PackageDirectory { get; }
    public string? UserDataDirectory { get; }
}
