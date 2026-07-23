using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Single host entry point for package discovery and lifecycle state; the application layer only presents it.</summary>
public sealed class PluginManagerRuntime
{
    private readonly PluginRuntimeHost runtimeHost;
    private readonly PluginPackageLifecycleRegistry registry;
    private readonly PluginActivationCoordinator activation;
    public PluginManagerRuntime(PluginRuntimeHost runtimeHost, PluginPackageLifecycleRegistry registry, PluginActivationCoordinator activation)
    {
        this.runtimeHost = runtimeHost ?? throw new ArgumentNullException(nameof(runtimeHost)); this.registry = registry ?? throw new ArgumentNullException(nameof(registry)); this.activation = activation ?? throw new ArgumentNullException(nameof(activation));
    }
    public PluginPackageLifecycleRegistry Registry => registry;
    /// <summary>Refreshes manifest-only package discovery.</summary>
    public IReadOnlyList<PluginPackageRuntimeRecord> Discover(string alacrityRoot)
    {
        foreach (var package in runtimeHost.Discover(alacrityRoot)) registry.Discover(package);
        return registry.Records;
    }
    /// <summary>Loads a package only after the caller supplies its host-computed trust decision.</summary>
    public PluginPackageRuntimeRecord LoadTrusted(PluginId id, PluginTrustVerificationResult trust, IPluginLogger logger, IMultiplayerSession multiplayer)
    {
        registry.MarkTrusted(id, trust);
        var record = registry.Records.Single(record => record.Manifest.Id == id);
        if (record.State == PluginPackageLifecycleState.Faulted) return record;
        registry.MarkLoaded(id, runtimeHost.LoadTrusted(record.Package, trust, logger, multiplayer));
        return registry.Records.Single(candidate => candidate.Manifest.Id == id);
    }
    /// <summary>Enables a loaded package through recovery and dependency gating.</summary>
    public PluginEnableResult Enable(PluginId id)
    {
        var controllers = registry.Records.Where(record => record.Controller != null).ToDictionary(record => record.Manifest.Id, record => record.Controller!);
        var result = activation.Enable(id, registry.Records.Where(record => record.State != PluginPackageLifecycleState.Uninstalled).Select(record => record.Manifest).ToArray(), controllers);
        foreach (var controller in controllers) registry.Synchronize(controller.Key);
        return result;
    }
    /// <summary>Disables a loaded enabled package without affecting other packages.</summary>
    public void Disable(PluginId id)
    {
        var record = registry.Records.Single(record => record.Manifest.Id == id);
        if (record.Controller?.State == PluginLifecycleState.Enabled) record.Controller.Disable();
        registry.Synchronize(id);
    }

    /// <summary>
    /// Requests a package reload. Loaded plugin assemblies cannot be unloaded independently from
    /// Terraria's AppDomain, so the host disables the package and records a restart requirement.
    /// </summary>
    public void RequestReload(PluginId id)
    {
        var record = registry.Records.Single(record => record.Manifest.Id == id);
        if (record.Controller?.State == PluginLifecycleState.Enabled)
            record.Controller.Disable();
        registry.MarkRestartRequired(id, "Plugin reload requires restarting Alacrity because loaded assemblies cannot be unloaded safely.");
    }

    /// <summary>Uninstalls an inactive package after lifecycle cleanup and dependency validation.</summary>
    public void Uninstall(PluginId id, PluginUninstallService uninstallService, bool removeUserData)
    {
        if (uninstallService == null) throw new ArgumentNullException(nameof(uninstallService));
        var record = registry.Records.Single(record => record.Manifest.Id == id);
        var enabledDependent = registry.Records.FirstOrDefault(candidate =>
            candidate.State == PluginPackageLifecycleState.Enabled &&
            candidate.Manifest.Dependencies.Any(dependency => dependency.Id == id));
        if (enabledDependent != null)
            throw new InvalidOperationException("Cannot uninstall " + id + " while enabled plugin " + enabledDependent.Manifest.Id + " requires it.");

        if (record.Controller != null && record.Controller.State != PluginLifecycleState.Uninstalled)
            record.Controller.Uninstall();

        uninstallService.Execute(uninstallService.Plan(id, removeUserData));
        registry.MarkUninstalled(id);
    }
}
