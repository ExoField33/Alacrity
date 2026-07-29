using System;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Host-owned exact-version admission for package manifests. Range support can
/// be added here later without changing package loading or plugin contracts.
/// </summary>
public static class PluginGameVersionCompatibility
{
    public static bool IsSupported(PluginManifest manifest, string currentGameVersion)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(currentGameVersion)) throw new ArgumentException("A current Terraria version is required.", nameof(currentGameVersion));
        return manifest.SupportedGameVersions.Any(version => string.Equals(version.Trim(), currentGameVersion.Trim(), StringComparison.Ordinal));
    }

    public static void EnsureSupported(PluginManifest manifest, string currentGameVersion)
    {
        if (!IsSupported(manifest, currentGameVersion))
            throw new PluginGameVersionCompatibilityException(manifest.Id, currentGameVersion, manifest.SupportedGameVersions);
    }
}

/// <summary>Host admission failure raised before a plugin assembly is loaded.</summary>
public sealed class PluginGameVersionCompatibilityException : InvalidOperationException
{
    public PluginGameVersionCompatibilityException(PluginId plugin, string currentGameVersion, System.Collections.Generic.IReadOnlyList<string> supportedVersions)
        : base("Plugin " + plugin + " supports Terraria " + string.Join(", ", supportedVersions) + ", but this Alacrity runtime is " + currentGameVersion + ".")
    {
        Plugin = plugin;
        CurrentGameVersion = currentGameVersion;
        SupportedVersions = supportedVersions ?? throw new ArgumentNullException(nameof(supportedVersions));
    }

    public PluginId Plugin { get; }
    public string CurrentGameVersion { get; }
    public System.Collections.Generic.IReadOnlyList<string> SupportedVersions { get; }
}
