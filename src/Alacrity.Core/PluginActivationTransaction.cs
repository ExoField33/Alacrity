using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Host-only activation coordinator for a single plugin enable operation. It is intentionally not
/// part of the SDK: plugin code receives scoped registrations rather than arbitrary host rollback
/// callbacks.
/// </summary>
public sealed class PluginActivationTransaction
{
    private readonly IPluginResourceScope resources;
    private readonly List<PluginActivationStep> steps = new List<PluginActivationStep>();
    private bool executed;

    /// <summary>Creates a transaction bound to the resource scope for this enable cycle.</summary>
    public PluginActivationTransaction(IPluginResourceScope resources)
    {
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    /// <summary>Adds a host-defined reversible activation step.</summary>
    public void AddStep(string name, Action activate, Action rollback)
    {
        if (executed)
            throw new InvalidOperationException("Activation steps cannot be added after execution begins.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A step name is required.", nameof(name));
        if (activate == null)
            throw new ArgumentNullException(nameof(activate));
        if (rollback == null)
            throw new ArgumentNullException(nameof(rollback));

        steps.Add(new PluginActivationStep(name, activate, rollback));
    }

    /// <summary>Runs all steps or reverses completed work after the first failure.</summary>
    public PluginActivationResult Execute()
    {
        if (executed)
            throw new InvalidOperationException("An activation transaction may execute only once.");
        executed = true;
        var completed = new List<PluginActivationStep>();

        try
        {
            foreach (var step in steps)
            {
                step.Activate();
                completed.Add(step);
            }

            return new PluginActivationResult(true, null, Array.Empty<PluginActivationRollbackFailure>());
        }
        catch (Exception activationFailure)
        {
            var rollbackFailures = RollBack(completed);
            try
            {
                resources.ReleaseAll();
            }
            catch (Exception cleanupFailure)
            {
                rollbackFailures.Add(new PluginActivationRollbackFailure("Release activation resources", cleanupFailure));
            }

            return new PluginActivationResult(false, activationFailure, rollbackFailures);
        }
    }

    private static List<PluginActivationRollbackFailure> RollBack(List<PluginActivationStep> completed)
    {
        var failures = new List<PluginActivationRollbackFailure>();
        for (var index = completed.Count - 1; index >= 0; index--)
        {
            var step = completed[index];
            try
            {
                step.Rollback();
            }
            catch (Exception exception)
            {
                failures.Add(new PluginActivationRollbackFailure(step.Name, exception));
            }
        }

        return failures;
    }

    private sealed class PluginActivationStep
    {
        public PluginActivationStep(string name, Action activate, Action rollback)
        {
            Name = name;
            Activate = activate;
            Rollback = rollback;
        }

        public string Name { get; }
        public Action Activate { get; }
        public Action Rollback { get; }
    }
}

/// <summary>Result of a host-owned plugin activation transaction.</summary>
public sealed class PluginActivationResult
{
    internal PluginActivationResult(bool succeeded, Exception? activationFailure, IEnumerable<PluginActivationRollbackFailure> rollbackFailures)
    {
        Succeeded = succeeded;
        ActivationFailure = activationFailure;
        RollbackFailures = rollbackFailures.ToArray();
    }

    /// <summary>Whether every activation step completed.</summary>
    public bool Succeeded { get; }
    /// <summary>Original failing activation exception, if any.</summary>
    public Exception? ActivationFailure { get; }
    /// <summary>Rollback and resource-cleanup failures retained after rollback attempts.</summary>
    public IReadOnlyList<PluginActivationRollbackFailure> RollbackFailures { get; }
    /// <summary>Whether a failed rollback leaves a restart or manual-recovery condition.</summary>
    public bool RequiresRecovery => !Succeeded && RollbackFailures.Count > 0;
}

/// <summary>One rollback failure from a failed host activation transaction.</summary>
public sealed class PluginActivationRollbackFailure
{
    internal PluginActivationRollbackFailure(string stepName, Exception exception)
    {
        StepName = stepName;
        Exception = exception;
    }

    /// <summary>Host step or cleanup action that failed.</summary>
    public string StepName { get; }
    /// <summary>Failure reported by that action.</summary>
    public Exception Exception { get; }
}
