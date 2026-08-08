using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Package-level state independent of a plugin's in-process lifecycle callbacks.</summary>
public enum PluginPackageLifecycleState
{
    Discovered,
    Trusted,
    Loaded,
    Enabled,
    Disabled,
    Faulted,
    RecoveryBlocked,
    Incompatible,
    RestartRequired,
    Uninstalled
}

/// <summary>Authoritative registry of discovered packages and their current host lifecycle state.</summary>
public sealed class PluginPackageLifecycleRegistry
{
    private readonly Dictionary<PluginId, PluginPackageRuntimeRecord> records = new Dictionary<PluginId, PluginPackageRuntimeRecord>();
    /// <summary>Current package records ordered for predictable application presentation.</summary>
    public IReadOnlyList<PluginPackageRuntimeRecord> Records => records.Values.OrderBy(record => record.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    /// <summary>Registers or refreshes a discovered manifest-only package.</summary>
    public PluginPackageRuntimeRecord Discover(PluginPackageDescriptor package)
    {
        if (package == null) throw new ArgumentNullException(nameof(package));
        if (records.TryGetValue(package.Manifest.Id, out var existing)) { existing.RefreshPackage(package); return existing; }
        var record = new PluginPackageRuntimeRecord(package);
        records.Add(package.Manifest.Id, record);
        return record;
    }
    /// <summary>Records the trust decision made before assembly loading.</summary>
    public void MarkTrusted(PluginId id, PluginTrustVerificationResult trust)
    {
        var record = Get(id); record.SetTrust(trust);
        record.State = trust.Level == PluginTrustLevel.Modified || trust.Level == PluginTrustLevel.Revoked || trust.Level == PluginTrustLevel.Expired ? PluginPackageLifecycleState.Faulted : PluginPackageLifecycleState.Trusted;
    }
    /// <summary>Associates a validated disabled controller with a loaded package.</summary>
    public void MarkLoaded(PluginId id, PluginLifecycleController controller) { var record = Get(id); record.Controller = controller ?? throw new ArgumentNullException(nameof(controller)); record.State = PluginPackageLifecycleState.Loaded; }
    /// <summary>Records a host-observed package load or lifecycle failure without exposing a mutable state path to plugins.</summary>
    public void MarkFaulted(PluginId id, string detail)
    {
        var record = Get(id);
        record.State = PluginPackageLifecycleState.Faulted;
        record.Detail = string.IsNullOrWhiteSpace(detail) ? "Package activation failed." : detail;
    }
    /// <summary>Synchronizes package state from its controller after enable/disable work.</summary>
    public void Synchronize(PluginId id)
    {
        var record = Get(id);
        if (record.Controller == null) return;
        record.State = record.Controller.State == PluginLifecycleState.Enabled ? PluginPackageLifecycleState.Enabled : record.Controller.State == PluginLifecycleState.Disabled ? PluginPackageLifecycleState.Disabled : PluginPackageLifecycleState.Faulted;
    }
    /// <summary>Marks activation unavailable until patch recovery is resolved.</summary>
    public void MarkRecoveryBlocked(PluginId id, string reason) { var record = Get(id); record.State = PluginPackageLifecycleState.RecoveryBlocked; record.Detail = reason; }
    /// <summary>Records host-side game-version rejection without treating package code as faulty.</summary>
    public void MarkIncompatible(PluginId id, string reason) { var record = Get(id); record.State = PluginPackageLifecycleState.Incompatible; record.Detail = reason; }
    /// <summary>Marks a package change that cannot become active until the next restart.</summary>
    public void MarkRestartRequired(PluginId id, string reason) { var record = Get(id); record.State = PluginPackageLifecycleState.RestartRequired; record.Detail = reason; }
    /// <summary>Marks a package removed after host-owned uninstall completes.</summary>
    public void MarkUninstalled(PluginId id) { var record = Get(id); record.State = PluginPackageLifecycleState.Uninstalled; record.Controller = null; }
    private PluginPackageRuntimeRecord Get(PluginId id) => records.TryGetValue(id, out var record) ? record : throw new KeyNotFoundException("Package is not registered: " + id);
}

/// <summary>Mutable host record for a package; plugins cannot set this state.</summary>
public sealed class PluginPackageRuntimeRecord
{
    internal PluginPackageRuntimeRecord(PluginPackageDescriptor package) { Package = package; Manifest = package.Manifest; State = PluginPackageLifecycleState.Discovered; }
    public PluginPackageDescriptor Package { get; private set; }
    public PluginManifest Manifest { get; private set; }
    public PluginTrustVerificationResult? Trust { get; private set; }
    public PluginLifecycleController? Controller { get; internal set; }
    public PluginPackageLifecycleState State { get; internal set; }
    public string? Detail { get; internal set; }
    internal void RefreshPackage(PluginPackageDescriptor package) { Package = package; Manifest = package.Manifest; if (State == PluginPackageLifecycleState.Uninstalled) State = PluginPackageLifecycleState.Discovered; }
    internal void SetTrust(PluginTrustVerificationResult trust) { Trust = trust; Detail = trust.Detail; }
}
