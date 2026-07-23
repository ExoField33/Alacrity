using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host activation boundary that enforces patch recovery and dependency gating before lifecycle callbacks.</summary>
public sealed class PluginActivationCoordinator
{
    private readonly PatchHost patchHost;
    private readonly PluginEnablePlanner planner;
    private readonly PluginEnableExecutor executor;
    private readonly PluginActivationGate gate;
    public PluginActivationCoordinator(PatchHost patchHost, PluginEnablePlanner planner, PluginEnableExecutor executor, PluginActivationGate gate)
    {
        this.patchHost = patchHost ?? throw new ArgumentNullException(nameof(patchHost));
        this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }
    /// <summary>Enables a package only after recovery and dependency policy both permit it.</summary>
    public PluginEnableResult Enable(PluginId requested, IReadOnlyList<PluginManifest> installed, IReadOnlyDictionary<PluginId, PluginLifecycleController> controllers)
    {
        if (installed == null) throw new ArgumentNullException(nameof(installed));
        if (controllers == null) throw new ArgumentNullException(nameof(controllers));
        var recovery = patchHost.RecoverIncompleteTransactions();
        if (recovery.Any(result => !result.IsResolved))
            return new PluginEnableResult(false, new InvalidOperationException("Plugin activation is blocked by unresolved patch recovery."), Array.Empty<PluginEnableNotification>(), Array.Empty<Exception>());
        var enabled = controllers.Where(pair => pair.Value.State == PluginLifecycleState.Enabled).Select(pair => pair.Key).ToArray();
        var plan = planner.Plan(requested, installed, enabled);
        var requestedManifest = installed.SingleOrDefault(manifest => manifest.Id == requested) ?? throw new InvalidOperationException("Requested plugin is not installed: " + requested + ".");
        if (!gate.CanActivate(requestedManifest, installed, new HashSet<PluginId>(plan.OrderedPlugins.Concat(enabled))))
            return new PluginEnableResult(false, new InvalidOperationException("Plugin activation is blocked by unresolved dependencies."), Array.Empty<PluginEnableNotification>(), Array.Empty<Exception>());
        return executor.Execute(plan, controllers);
    }
}
