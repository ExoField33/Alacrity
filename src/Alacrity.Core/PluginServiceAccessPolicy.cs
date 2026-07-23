using System;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Central decision point for issuing sensitive host services. This is capability control only;
/// ordinary in-process managed plugins are not a complete security sandbox.
/// </summary>
public sealed class PluginServiceAccessPolicy
{
    /// <summary>Checks whether a package may receive an owner-bound managed patch service.</summary>
    public bool CanIssueManagedPatchService(PluginManifest manifest, PluginTrustLevel trustLevel, bool policyAllowsManagedPatches, bool runtimeDisableSupported, out string reason)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (!manifest.Permissions.HasFlag(PluginPermission.ManagedPatch))
        {
            reason = "The verified manifest does not request ManagedPatch permission.";
            return false;
        }
        if (!policyAllowsManagedPatches)
        {
            reason = "The current host or server policy denies managed patches.";
            return false;
        }
        if (!runtimeDisableSupported)
        {
            reason = "Managed patches require a runtime-disable-safe host path.";
            return false;
        }
        if (trustLevel != PluginTrustLevel.Official && trustLevel != PluginTrustLevel.VerifiedThirdParty && trustLevel != PluginTrustLevel.LocallyTrusted)
        {
            reason = "The package trust result does not permit managed patches.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

/// <summary>Host factory that issues patch capabilities only after a policy decision succeeds.</summary>
public sealed class PluginPatchServiceFactory
{
    private readonly PatchHost patchHost;
    private readonly PluginServiceAccessPolicy policy;

    public PluginPatchServiceFactory(PatchHost patchHost, PluginServiceAccessPolicy policy)
    {
        this.patchHost = patchHost ?? throw new ArgumentNullException(nameof(patchHost));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>Issues a patch capability or returns a policy denial without loading plugin code.</summary>
    public bool TryCreate(PluginManifest manifest, PluginTrustLevel trustLevel, bool policyAllowsManagedPatches, bool runtimeDisableSupported, out IPatchEngine? patchService, out string denialReason)
    {
        if (!policy.CanIssueManagedPatchService(manifest, trustLevel, policyAllowsManagedPatches, runtimeDisableSupported, out denialReason))
        {
            patchService = null;
            return false;
        }

        patchService = patchHost.ForPlugin(manifest.Id);
        return true;
    }
}
