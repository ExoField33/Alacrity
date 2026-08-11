using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Coordinates legacy synchronous plugin callbacks while keeping host-owned cleanup deterministic.
/// Callback failures are rethrown intact; cleanup failures are retained separately for diagnostics.
/// </summary>
public sealed class PluginLifecycleController : IDisposable
{
    private readonly IAlacrityPlugin? plugin;
    private readonly IAsyncAlacrityPlugin? asyncPlugin;
    private IPluginContext context;
    private readonly Func<IPluginContext>? contextFactory;
    private readonly TimeSpan asyncCallbackTimeout;
    private bool initialized;
    private bool hasInitialized;
    private bool shutdownCalled;
    private bool shutdown;
    private int asynchronousDisposeStarted;
    private int synchronousQuiescencePending;
    private int asynchronousCallbackRunning;
    private int asynchronousCallbackQuarantined;
    private Task? quarantinedAsyncCallback;

    public PluginLifecycleController(IAlacrityPlugin plugin, IPluginContext context)
        : this((object)plugin, context, default(TimeSpan?), null)
    {
    }

    public PluginLifecycleController(IAsyncAlacrityPlugin plugin, IPluginContext context, TimeSpan? asyncCallbackTimeout = null)
        : this((object)plugin, context, asyncCallbackTimeout, null)
    {
    }

    /// <summary>Creates one lifecycle state machine for exactly one supported callback contract.</summary>
    public PluginLifecycleController(object plugin, IPluginContext context, TimeSpan? asyncCallbackTimeout = null)
        : this(plugin, context, asyncCallbackTimeout, null)
    {
    }

    /// <summary>
    /// Creates a lifecycle controller whose host supplies a fresh context for every activation after
    /// the first. This prevents released, scope-guarded services from leaking into a later enable.
    /// </summary>
    public PluginLifecycleController(object plugin, IPluginContext context, Func<IPluginContext> contextFactory, TimeSpan? asyncCallbackTimeout = null)
        : this(plugin, context, asyncCallbackTimeout, contextFactory)
    {
    }

    private PluginLifecycleController(object plugin, IPluginContext context, TimeSpan? asyncCallbackTimeout, Func<IPluginContext>? contextFactory)
    {
        if (plugin == null) throw new ArgumentNullException(nameof(plugin));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        if (context.Manifest == null)
            throw new ArgumentException("A host-owned manifest is required.", nameof(context));
        this.plugin = plugin as IAlacrityPlugin;
        this.asyncPlugin = plugin as IAsyncAlacrityPlugin;
        if ((this.plugin == null) == (this.asyncPlugin == null))
            throw new ArgumentException("A plugin entry must implement exactly one lifecycle contract.", nameof(plugin));
        this.asyncCallbackTimeout = asyncCallbackTimeout ?? TimeSpan.FromSeconds(5);
        this.contextFactory = contextFactory;
        if (this.asyncCallbackTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(asyncCallbackTimeout));
        State = PluginLifecycleState.Discovered;
        LastOperation = new PluginOperationResult("Construct", State, null, null);
    }

    public PluginLifecycleState State { get; private set; }

    /// <summary>The manifest loaded by the host from plugin.json.</summary>
    public PluginManifest Manifest => context.Manifest;

    /// <summary>Diagnostic outcome of the most recent lifecycle operation.</summary>
    public PluginOperationResult LastOperation { get; private set; }

    /// <summary>Whether this controller invokes the asynchronous callback contract.</summary>
    public bool UsesAsyncLifecycle => asyncPlugin != null;

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
        EnsureSynchronousLifecycle();
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Disabled);
        try
        {
            PrepareContextForInitialization();
            plugin!.Initialize(context);
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
        EnsureSynchronousLifecycle();
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
            plugin!.Enable();
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
        EnsureSynchronousLifecycle();
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Enabled);
        Transition(PluginLifecycleState.Disabling);
        CloseActivationCallbackAdmission();

        Task<bool>? backgroundDrain = BeginBackgroundDrain(out var cleanupFailures);
        Exception? callbackFailure = null;
        try
        {
            plugin!.Disable();
        }
        catch (Exception exception)
        {
            callbackFailure = exception;
        }

        cleanupFailures.AddRange(ForceReleaseResources());
        initialized = false;
        if (callbackFailure != null || cleanupFailures.Count != 0)
        {
            State = PluginLifecycleState.Faulted;
        }
        else if (backgroundDrain == null)
        {
            State = PluginLifecycleState.Disabled;
        }
        else
        {
            // The activation is already closed to new work, but the plugin object cannot be reused
            // until worker code that entered before cancellation has actually left.
            Interlocked.Exchange(ref synchronousQuiescencePending, 1);
            ObserveSynchronousQuiescence(backgroundDrain);
        }

        Record("Disable", callbackFailure, cleanupFailures);
        Rethrow(callbackFailure, cleanupFailures);
    }

    public void Uninstall()
    {
        EnsureSynchronousLifecycle();
        EnsureNotShutdown();
        CloseActivationCallbackAdmission();
        var cleanupFailures = RequestBackgroundDrain();
        Exception? callbackFailure = null;
        State = PluginLifecycleState.Uninstalling;

        if (initialized && hasInitialized)
            InvokeCallback("Disable", plugin!.Disable, ref callbackFailure);

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

        if (UsesAsyncLifecycle)
        {
            BeginAsynchronousDispose();
            return;
        }

        CloseActivationCallbackAdmission();
        var cleanupFailures = RequestBackgroundDrain();
        Exception? callbackFailure = null;
        try
        {
            if (initialized && hasInitialized)
                InvokeCallback("Disable", plugin!.Disable, ref callbackFailure);
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

    /// <summary>
    /// <see cref="IDisposable.Dispose"/> has no asynchronous completion channel. Starting a bounded
    /// async teardown here keeps accidental disposal from freezing Terraria's update/render thread;
    /// explicit host shutdown paths use <see cref="DisposeAsync"/> when they need to await it.
    /// </summary>
    private void BeginAsynchronousDispose()
    {
        if (Interlocked.Exchange(ref asynchronousDisposeStarted, 1) != 0)
        {
            return;
        }

        Task disposal;
        try
        {
            disposal = DisposeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            context.Logger.Error("Asynchronous plugin disposal could not be started.", exception);
            return;
        }

        disposal.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    context.Logger.Error("Asynchronous plugin disposal failed.", completed.Exception!.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private List<PluginCleanupFailure> ForceReleaseResources()
    {
        try
        {
            CloseActivationCallbackAdmission();
            // Runtime activations receive a context factory and never reuse a scope. Legacy
            // direct-controller callers remain releasable because they cannot supply a fresh one.
            if (contextFactory != null)
                context.Resources.Dispose();
            else
                context.Resources.ReleaseAll();
            return new List<PluginCleanupFailure>();
        }
        catch (Exception exception)
        {
            return new List<PluginCleanupFailure> { new PluginCleanupFailure("Release resources", exception) };
        }
    }

    /// <summary>
    /// Teardown is an admission boundary, not a blocking barrier. Existing callbacks may finish,
    /// but no callback that has not acquired a lease may enter the old activation afterwards.
    /// </summary>
    private void CloseActivationCallbackAdmission()
    {
        if (context is IActivationCallbackAdmissionContext activation)
        {
            activation.CloseCallbackAdmission();
        }
    }

    /// <summary>
    /// Begins cancellation for synchronous lifecycle teardown without blocking the game thread.
    /// The released activation rejects all old services immediately; cooperative worker completion
    /// is observed in the background and attributed to the owning plugin logger.
    /// </summary>
    private List<PluginCleanupFailure> RequestBackgroundDrain()
    {
        try
        {
            if (context is not IActivationBackgroundWorkContext activation)
            {
                return new List<PluginCleanupFailure>();
            }

            Task<bool> drain = activation.StopAndDrainBackgroundWorkAsync(asyncCallbackTimeout);
            ObserveBackgroundDrain(drain);
            return new List<PluginCleanupFailure>();
        }
        catch (Exception exception)
        {
            return new List<PluginCleanupFailure> { new PluginCleanupFailure("Drain activation background work", exception) };
        }
    }

    private Task<bool>? BeginBackgroundDrain(out List<PluginCleanupFailure> cleanupFailures)
    {
        cleanupFailures = new List<PluginCleanupFailure>();
        try
        {
            if (context is not IActivationBackgroundWorkContext activation)
            {
                return null;
            }

            return activation.StopAndDrainBackgroundWorkAsync(asyncCallbackTimeout);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(new PluginCleanupFailure("Drain activation background work", exception));
            return null;
        }
    }

    private void ObserveSynchronousQuiescence(Task<bool> drain)
    {
        _ = CompleteSynchronousQuiescenceAsync(drain);
    }

    private async Task CompleteSynchronousQuiescenceAsync(Task<bool> drain)
    {
        try
        {
            bool quiesced = await drain.ConfigureAwait(false);
            if (State != PluginLifecycleState.Disabling || Interlocked.Exchange(ref synchronousQuiescencePending, 0) == 0)
            {
                return;
            }

            if (quiesced)
            {
                State = PluginLifecycleState.Disabled;
                Record("Disable quiescence", null, null);
                return;
            }

            var timeout = new TimeoutException("Activation background work did not stop before the host timeout. The plugin instance cannot be reactivated in this process.");
            State = PluginLifecycleState.Faulted;
            Record("Disable quiescence", timeout, null);
            context.Logger.Warn(timeout.Message);
        }
        catch (Exception exception)
        {
            if (State != PluginLifecycleState.Disabling || Interlocked.Exchange(ref synchronousQuiescencePending, 0) == 0)
            {
                return;
            }

            State = PluginLifecycleState.Faulted;
            Record("Disable quiescence", exception, null);
            context.Logger.Error("Activation background work failed while completing synchronous plugin teardown.", exception);
        }
    }

    private async Task<List<PluginCleanupFailure>> DrainBackgroundWorkAsync()
    {
        try
        {
            CloseActivationCallbackAdmission();
            if (context is not IActivationBackgroundWorkContext activation ||
                await activation.StopAndDrainBackgroundWorkAsync(asyncCallbackTimeout).ConfigureAwait(false))
                return new List<PluginCleanupFailure>();
            return new List<PluginCleanupFailure> { new PluginCleanupFailure("Drain activation background work", new TimeoutException("Activation background work did not stop before the host timeout.")) };
        }
        catch (Exception exception)
        {
            return new List<PluginCleanupFailure> { new PluginCleanupFailure("Drain activation background work", exception) };
        }
    }

    private void ObserveBackgroundDrain(Task<bool> drain)
    {
        _ = ObserveBackgroundDrainAsync(drain);
    }

    private async Task ObserveBackgroundDrainAsync(Task<bool> drain)
    {
        try
        {
            if (!await drain.ConfigureAwait(false))
            {
                context.Logger.Warn("Activation background work exceeded the shutdown timeout after the plugin scope was released.");
            }
        }
        catch (Exception exception)
        {
            context.Logger.Error("Activation background work failed while completing teardown.", exception);
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
            plugin!.Shutdown();
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

    /// <summary>Initializes either lifecycle contract without scheduling synchronous plugins onto worker threads.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!UsesAsyncLifecycle) { Initialize(); return; }
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Disabled);
        try
        {
            PrepareContextForInitialization();
            await InvokeAsync("Initialize", token => asyncPlugin!.InitializeAsync(context, token), cancellationToken).ConfigureAwait(false);
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

    /// <summary>Enables either lifecycle contract after initialization.</summary>
    public async Task EnableAsync(CancellationToken cancellationToken)
    {
        if (!UsesAsyncLifecycle) { Enable(); return; }
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Disabled);
        if (!initialized)
        {
            if (!hasInitialized) throw new InvalidOperationException("A plugin must be initialized before it can be enabled.");
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        Transition(PluginLifecycleState.Enabling);
        try
        {
            await InvokeAsync("Enable", token => asyncPlugin!.EnableAsync(token), cancellationToken).ConfigureAwait(false);
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

    /// <summary>Disables an asynchronous plugin with cancellation and bounded callback wait time.</summary>
    public async Task DisableAsync(CancellationToken cancellationToken)
    {
        if (!UsesAsyncLifecycle) { Disable(); return; }
        EnsureNotShutdown();
        EnsureState(PluginLifecycleState.Enabled);
        Transition(PluginLifecycleState.Disabling);
        CloseActivationCallbackAdmission();
        var cleanupFailures = await DrainBackgroundWorkAsync().ConfigureAwait(false);
        Exception? callbackFailure = null;
        try { await InvokeAsync("Disable", token => asyncPlugin!.DisableAsync(token), cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { callbackFailure = exception; }

        cleanupFailures.AddRange(ForceReleaseResources());
        initialized = false;
        State = callbackFailure == null && cleanupFailures.Count == 0 ? PluginLifecycleState.Disabled : PluginLifecycleState.Faulted;
        Record("Disable", callbackFailure, cleanupFailures);
        Rethrow(callbackFailure, cleanupFailures);
    }

    /// <summary>Shuts down an asynchronous plugin and always releases host-owned resources.</summary>
    public async Task DisposeAsync(CancellationToken cancellationToken)
    {
        if (!UsesAsyncLifecycle) { Dispose(); return; }
        if (shutdown) return;
        CloseActivationCallbackAdmission();
        var cleanupFailures = await DrainBackgroundWorkAsync().ConfigureAwait(false);
        Exception? callbackFailure = null;
        try
        {
            if (initialized && hasInitialized)
            {
                try { await InvokeAsync("Disable", token => asyncPlugin!.DisableAsync(token), cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { callbackFailure = exception; }
            }
            cleanupFailures.AddRange(ForceReleaseResources());
            if (hasInitialized && !shutdownCalled)
            {
                try { await InvokeAsync("Shutdown", token => asyncPlugin!.ShutdownAsync(token), cancellationToken).ConfigureAwait(false); }
                catch (Exception exception)
                {
                    if (callbackFailure == null) callbackFailure = exception;
                    else cleanupFailures.Add(new PluginCleanupFailure("Shutdown", exception));
                }
                finally { shutdownCalled = true; }
            }
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

    /// <summary>Uninstalls an asynchronous plugin after its callbacks and owned resources have been released.</summary>
    public async Task UninstallAsync(CancellationToken cancellationToken)
    {
        if (!UsesAsyncLifecycle) { Uninstall(); return; }
        EnsureNotShutdown();
        State = PluginLifecycleState.Uninstalling;
        CloseActivationCallbackAdmission();
        var cleanupFailures = await DrainBackgroundWorkAsync().ConfigureAwait(false);
        Exception? callbackFailure = null;
        try
        {
            if (initialized && hasInitialized)
            {
                try { await InvokeAsync("Disable", token => asyncPlugin!.DisableAsync(token), cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { callbackFailure = exception; }
            }
            cleanupFailures.AddRange(ForceReleaseResources());
            if (hasInitialized && !shutdownCalled)
            {
                try { await InvokeAsync("Shutdown", token => asyncPlugin!.ShutdownAsync(token), cancellationToken).ConfigureAwait(false); }
                catch (Exception exception)
                {
                    if (callbackFailure == null) callbackFailure = exception;
                    else cleanupFailures.Add(new PluginCleanupFailure("Shutdown", exception));
                }
                finally { shutdownCalled = true; }
            }
            cleanupFailures.AddRange(ForceDisposeResources());
        }
        finally
        {
            initialized = false;
            shutdown = true;
            State = PluginLifecycleState.Uninstalled;
            Record("Uninstall", callbackFailure, cleanupFailures);
        }
        Rethrow(callbackFailure, cleanupFailures);
    }

    private async Task InvokeAsync(string operation, Func<CancellationToken, Task> callback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref asynchronousCallbackQuarantined) != 0)
        {
            throw new InvalidOperationException("The plugin instance is quarantined because a previous asynchronous lifecycle callback did not stop. It cannot receive another lifecycle callback in this process.");
        }

        if (Interlocked.CompareExchange(ref asynchronousCallbackRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("An asynchronous lifecycle callback is already running for this plugin instance.");
        }

        using var timeout = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var timerCancellation = new CancellationTokenSource();
        Task task;
        try
        {
            task = callback(linked.Token) ?? throw new InvalidOperationException(operation + " returned a null task.");
        }
        catch
        {
            Interlocked.Exchange(ref asynchronousCallbackRunning, 0);
            throw;
        }

        _ = task.ContinueWith(
            _ => Interlocked.Exchange(ref asynchronousCallbackRunning, 0),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Task timeoutTask = Task.Delay(asyncCallbackTimeout, timerCancellation.Token);
        Task externalCancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        Task completed = await Task.WhenAny(task, timeoutTask, externalCancellationTask).ConfigureAwait(false);
        if (ReferenceEquals(completed, task))
        {
            timerCancellation.Cancel();
            await task.ConfigureAwait(false);
            return;
        }

        timeout.Cancel();
        ObserveFault(task);
        QuarantineAsyncCallback(operation, task, ReferenceEquals(completed, externalCancellationTask));
        if (ReferenceEquals(completed, externalCancellationTask))
            throw new OperationCanceledException(cancellationToken);
        throw new TimeoutException(operation + " exceeded the host callback timeout of " + asyncCallbackTimeout + ".");
    }

    private void QuarantineAsyncCallback(string operation, Task task, bool externalCancellation)
    {
        if (Interlocked.Exchange(ref asynchronousCallbackQuarantined, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref quarantinedAsyncCallback, task);

        string reason = externalCancellation
            ? operation + " was cancelled while the plugin callback was still running."
            : operation + " exceeded the host timeout while the plugin callback was still running.";
        context.Logger.Warn(reason + " The plugin instance is quarantined and will not receive another lifecycle callback in this process.");
        _ = task.ContinueWith(
            completed =>
            {
                Interlocked.CompareExchange(ref quarantinedAsyncCallback, null, completed);
                if (completed.IsFaulted)
                {
                    context.Logger.Error("A quarantined asynchronous " + operation + " callback later faulted.", completed.Exception!.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveFault(Task task)
    {
        task.ContinueWith(completed => _ = completed.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
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

    private void PrepareContextForInitialization()
    {
        if (!hasInitialized)
        {
            return;
        }

        if (contextFactory == null)
        {
            throw new InvalidOperationException(
                "This lifecycle controller was constructed with one fixed plugin context and cannot be reactivated after disable. " +
                "Construct it with a fresh context factory for each activation.");
        }

        IPluginContext replacement = contextFactory() ?? throw new InvalidOperationException("The plugin context factory returned null.");
        if (replacement.Manifest == null || replacement.Manifest.Id != Manifest.Id)
            throw new InvalidOperationException("The plugin context factory returned a context for a different manifest.");
        context = replacement;
    }

    private void EnsureSynchronousLifecycle()
    {
        if (UsesAsyncLifecycle)
            throw new InvalidOperationException("This plugin uses IAsyncAlacrityPlugin. Use the asynchronous lifecycle methods.");
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
