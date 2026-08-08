using System;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Checks package requirements before executable plugin code is loaded.</summary>
public static class PluginCompatibilityValidator
{
    public static void EnsureSupported(PluginManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        PluginCompatibilityRequirements requirements = manifest.Compatibility;
        if (requirements.PluginSdkVersion != AlacrityCompatibility.PluginSdk)
            throw new PluginCompatibilityException(manifest.Id, "PluginSdk", requirements.PluginSdkVersion, AlacrityCompatibility.PluginSdk);
        if (requirements.HostVersion != AlacrityCompatibility.Host)
            throw new PluginCompatibilityException(manifest.Id, "Core host", requirements.HostVersion, AlacrityCompatibility.Host);
        if (requirements.BridgeAbiVersion != AlacrityCompatibility.BridgeAbi)
            throw new PluginCompatibilityException(manifest.Id, "Terraria bridge ABI", requirements.BridgeAbiVersion, AlacrityCompatibility.BridgeAbi);
    }
}

/// <summary>Clear package admission failure that identifies the stale participant.</summary>
public sealed class PluginCompatibilityException : InvalidOperationException
{
    public PluginCompatibilityException(PluginId plugin, string component, int expected, int actual)
        : base("Plugin '" + plugin.Value + "' requires " + component + " compatibility " + expected + ", but this client provides " + actual + ". Rebuild or update the plugin and client assemblies together.")
    {
        Plugin = plugin; Component = component; Expected = expected; Actual = actual;
    }
    public PluginId Plugin { get; }
    public string Component { get; }
    public int Expected { get; }
    public int Actual { get; }
}
