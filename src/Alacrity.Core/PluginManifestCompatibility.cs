using System;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Guards the temporary legacy plugin manifest property. Plugin code cannot widen its identity,
/// permissions, or multiplayer classification beyond the package manifest verified by the host.
/// </summary>
internal static class PluginManifestCompatibility
{
    public static void EnsureLegacyPluginMatchesHost(PluginManifest legacyManifest, PluginManifest hostManifest)
    {
        if (legacyManifest == null)
            throw new ArgumentNullException(nameof(legacyManifest));
        if (hostManifest == null)
            throw new ArgumentNullException(nameof(hostManifest));

        if (legacyManifest.Id != hostManifest.Id ||
            !Equals(legacyManifest.Version, hostManifest.Version) ||
            !string.Equals(legacyManifest.Publisher, hostManifest.Publisher, StringComparison.Ordinal) ||
            legacyManifest.Capabilities != hostManifest.Capabilities ||
            legacyManifest.Permissions != hostManifest.Permissions ||
            legacyManifest.MultiplayerSafety != hostManifest.MultiplayerSafety ||
            legacyManifest.RequiresServerSupport != hostManifest.RequiresServerSupport ||
            !legacyManifest.SupportedGameVersions.SequenceEqual(hostManifest.SupportedGameVersions, StringComparer.Ordinal) ||
            !DependenciesMatch(legacyManifest, hostManifest))
        {
            throw new InvalidOperationException("The legacy plugin manifest does not match the host-verified plugin.json manifest.");
        }
    }

    private static bool DependenciesMatch(PluginManifest left, PluginManifest right)
    {
        if (left.Dependencies.Count != right.Dependencies.Count)
            return false;

        for (var index = 0; index < left.Dependencies.Count; index++)
        {
            var leftDependency = left.Dependencies[index];
            var rightDependency = right.Dependencies[index];
            if (leftDependency.Id != rightDependency.Id || !Equals(leftDependency.MinimumVersion, rightDependency.MinimumVersion))
                return false;
        }

        return true;
    }
}
