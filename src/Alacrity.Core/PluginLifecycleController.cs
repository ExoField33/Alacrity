using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Coordinates legacy synchronous plugin callbacks while keeping host-owned cleanup deterministic.
/// Callback failures are rethrown intact; cleanup failures are retained separately for diagnostics.
/// </summary>
public sealed class PluginLifecycleController : IDisposable
{
    private readonly IAlacrityPlugin plugin;
    private readonly IPluginContext context;
    private bool initialized;
    private bool hasInitialized;
    private bool shutdownCalled;
    private bool shutdown;

    public PluginLifecycleController(IAlacrityPlugin plugin, IPluginContext context)
    {
        this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        if (context.Manifest == null)
            throw new ArgumentException("A host-owned manifest is required.", nameof(context));
#pragma warning disable CS0618 // Core owns the temporary legacy-manifest compatibility boundary.
        PluginManifestCompatibility.EnsureLegacyPluginMatchesHost(plugin.Manifest, context.Manifest);
#pragma warning restore CS0618
        State = PluginLifecycleState.Discovered;
        LastOperation = new PluginOperationResult("Construct", State, null, null);
    }

    public PluginLifecycleState State { get; private set; }

    /// <summary>The manifest loaded by the host from plugin.json.</summary>
    public PluginManifest Manifest => context.Manifest;

    /// <summary>Diagnostic outcome of the most recent lifecycle operation.</summary>
    public PluginOperationResult LastOperation { get; private set; }

    public void Validate()
    {
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Discovered);
        Transition(PluginLifecycleState.Validating);
        try
        {
            context.Manifest.Validate();
            Transition(PluginLifecycleState.Disabled);
            Record("Validate", null, null);
        }
        catch (Exception exception)
        {
            State = PluginLifecycleState.Faulted;
            Record("Validate", exception, null);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public void Initialize()
    {
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Disabled);
        try
        {
            plugin.Initialize(context);
            initialized = true;
            hasInitialized = true;
            Record("Initialize", null, null);
        }
        catch (Exception exception)
        {
            initialized = false;
            State = PluginLifecycleState.Faulted;
            var cleanupFailures = ForceReleaseResources();
            Record("Initialize", exception, cleanupFailures);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public void Enable()
    {
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Disabled);
        if (!initialized)
        {
            if (!hasInitialized)
                throw new InvalidOperationException("A plugin must be initialized before it can be enabled.");

            Initialize();
        }

        Transition(PluginLifecycleState.Enabling);
        try
        {
            plugin.Enable();
            Transition(PluginLifecycleState.Enabled);
            Record("Enable", null, null);
        }
        catch (Exception exception)
        {
            initialized = false;
            State = PluginLifecycleState.Faulted;
            var cleanupFailures = ForceReleaseResources();
            Record("Enable", exception, cleanupFailures);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public void Disable()
    {
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Enabled);
        Transition(PluginLifecycleState.Disabling);

        Exception? callbackFailure = null;
        try
        {
            plugin.Disable();
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }

        var cleanupFailures = ForceReleaseResources();
        initialized = false;
        State = callbackFailure == null && cleanupFailures.Count == 0
            ? PluginLifecycleState.Disabled
            : PluginLifecycleState.Faulted;
        Record("Disable", callbackFailure, cleanupFailures);
        Rethrow(callbackFailure, cleanupFailures);
    }

    public void Uninstall()
    {
        EnsureNotShutdown();
        var cleanupFailures = new List<PluginCleanupFailure>();
        Exception? callbackFailure = null;
        State = PluginLifecycleState.Uninstalling;

        if (initialized && hasInitialized)
            InvokeCallback("Disable", plugin.Disable, ref callbackFailure);

        cleanupFailures.AddRange(ForceReleaseResources());
        if (hasInitialized && !shutdownCalled)
            InvokeShutdown(cleanupFailures, ref callbackFailure);
        cleanupFailures.AddRange(ForceDisposeResources());

        // Uninstallation must reach a terminal state even when a plugin is faulty.
        initialized = false;
        shutdown = true;
        State = PluginLifecycleState.Uninstalled;
        Record("Uninstall", callbackFailure, cleanupFailures);
        Rethrow(callbackFailure, cleanupFailures);
    }

    public void Dispose()
    {
        if (shutdown)
            return;

        var cleanupFailures = new List<PluginCleanupFailure>();
        Exception? callbackFailure = null;
        try
        {
            if (initialized && hasInitialized)
                InvokeCallback("Disable", plugin.Disable, ref callbackFailure);
            cleanupFailures.AddRange(ForceReleaseResources());
            if (hasInitialized && !shutdownCalled)
                InvokeShutdown(cleanupFailures, ref callbackFailure);
            cleanupFailures.AddRange(ForceDisposeResources());
        }
        finally
        {
            initialized = false;
            shutdown = true;
            State = PluginLifecycleState.Uninstalled;
            Record("Dispose", callbackFailure, cleanupFailures);
        }
    }

    private List<PluginCleanupFailure> ForceReleaseResources()
    {
        try
        {
            context.Resources.ReleaseAll();
            return new List<PluginCleanupFailure>();
        }
        catch (Exception exception)
        {
            return new List<PluginCleanupFailure> { new PluginCleanupFailure("Release resources", exception) };
        }
    }

    private List<PluginCleanupFailure> ForceDisposeResources()
    {
        try
        {
            context.Resources.Dispose();
            return new List<PluginCleanupFailure>();
        }
        catch (Exception exception)
        {
            return new List<PluginCleanupFailure> { new PluginCleanupFailure("Dispose resources", exception) };
        }
    }

    private void InvokeShutdown(List<PluginCleanupFailure> cleanupFailures, ref Exception? callbackFailure)
    {
        try
        {
            plugin.Shutdown();
        }
        catch (Exception exception)
        {
            if (callbackFailure == null)
                callbackFailure = exception;
            else
                cleanupFailures.Add(new PluginCleanupFailure("Shutdown", exception));
        }
        finally
        {
            shutdownCalled = true;
        }
    }

    private static void InvokeCallback(string operation, Action callback, ref Exception? callbackFailure)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            if (callbackFailure == null)
                callbackFailure = exception;
        }
    }

    private void Record(string operation, Exception? callbackFailure, IEnumerable<PluginCleanupFailure>? cleanupFailures)
    {
        LastOperation = new PluginOperationResult(operation, State, callbackFailure, cleanupFailures);
    }

    private static void Rethrow(Exception? callbackFailure, List<PluginCleanupFailure> cleanupFailures)
    {
        if (callbackFailure != null)
            ExceptionDispatchInfo.Capture(callbackFailure).Throw();
        if (cleanupFailures.Count > 0)
            ExceptionDispatchInfo.Capture(cleanupFailures[0].Exception).Throw();
    }

    private void EnsureNotShutdown()
    {
        if (shutdown || State == PluginLifecycleState.Uninstalled)
            throw new ObjectDisposedException(nameof(PluginLifecycleController));
    }

    private void EnsureState(PluginLifecycleState expected)
    {
        if (State != expected)
            throw new InvalidOperationException("Expected lifecycle state " + expected + ", got " + State + ".");
    }

    private void Transition(PluginLifecycleState state)
    {
        State = state;
    }
}
