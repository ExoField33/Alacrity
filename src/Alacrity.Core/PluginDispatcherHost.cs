using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned main-thread queue. Integration calls <see cref="Drain"/> at its verified update boundary.</summary>
public sealed class PluginDispatcherHost
{
    private readonly object gate = new object();
    private readonly Queue<WorkItem> pending = new Queue<WorkItem>();
    private readonly int mainThreadId = Environment.CurrentManagedThreadId;
    private readonly int maximumCallbacksPerDrain;
    private readonly TimeSpan maximumDrainTime;

    public PluginDispatcherHost(int maximumCallbacksPerDrain = 64, TimeSpan? maximumDrainTime = null)
    {
        if (maximumCallbacksPerDrain <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCallbacksPerDrain));
        this.maximumCallbacksPerDrain = maximumCallbacksPerDrain;
        this.maximumDrainTime = maximumDrainTime ?? TimeSpan.FromMilliseconds(2);
        if (this.maximumDrainTime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumDrainTime));
    }

    public IPluginDispatcher CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginLogger? logger = null)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        var guard = new ScopeGuard();
        try
        {
            resources.Own("dispatcher", PluginResourceKind.BackgroundTask, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }
        return new ScopedDispatcher(this, resources, guard, manifest.Id, logger);
    }

    /// <summary>Runs queued work on the main thread. Cancelled scope-owned work is skipped.</summary>
    public void Drain(Action<Exception>? reportFailure = null)
    {
        if (Environment.CurrentManagedThreadId != mainThreadId)
            throw new InvalidOperationException("Plugin dispatcher can only drain on its owning main thread.");
        var stopwatch = Stopwatch.StartNew();
        int executed = 0;
        while (executed < maximumCallbacksPerDrain && stopwatch.Elapsed < maximumDrainTime)
        {
            WorkItem item;
            lock (gate)
            {
                if (pending.Count == 0) return;
                item = pending.Dequeue();
            }
            if (item.Registration.IsReleased) continue;
            try { item.Callback(); }
            catch (Exception exception)
            {
                item.Logger?.Error("Dispatcher callback '" + item.Name + "' failed for plugin '" + item.Owner.Value + "'.", exception);
                reportFailure?.Invoke(exception);
            }
            finally { item.Registration.Dispose(); }
            executed++;
        }
    }

    private IPluginRegistration Post(IPluginResourceScope resources, ScopeGuard guard, PluginId owner, IPluginLogger? logger, Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        if (guard.IsReleased) throw new ObjectDisposedException("IPluginDispatcher", "The owning plugin scope has been released.");
        var registration = new WorkRegistration();
        resources.Own("dispatcher-work", PluginResourceKind.BackgroundTask, registration);
        lock (gate) pending.Enqueue(new WorkItem(callback, registration, owner, logger));
        return registration;
    }

    private sealed class ScopedDispatcher : IPluginDispatcher
    {
        private readonly PluginDispatcherHost host;
        private readonly IPluginResourceScope resources;
        private readonly ScopeGuard guard;
        private readonly PluginId owner;
        private readonly IPluginLogger? logger;
        public ScopedDispatcher(PluginDispatcherHost host, IPluginResourceScope resources, ScopeGuard guard, PluginId owner, IPluginLogger? logger) { this.host = host; this.resources = resources; this.guard = guard; this.owner = owner; this.logger = logger; }
        public bool IsMainThread => Environment.CurrentManagedThreadId == host.mainThreadId;
        public IPluginRegistration Post(Action callback) => host.Post(resources, guard, owner, logger, callback);
    }

    private sealed class WorkItem { public WorkItem(Action callback, WorkRegistration registration, PluginId owner, IPluginLogger? logger) { Callback = callback; Registration = registration; Owner = owner; Logger = logger; } public Action Callback { get; } public WorkRegistration Registration { get; } public PluginId Owner { get; } public IPluginLogger? Logger { get; } public string Name => Registration.Name; }
    private sealed class WorkRegistration : IPluginRegistration { private int released; public string Name => "dispatcher-work"; public bool IsReleased => Volatile.Read(ref released) != 0; public void Dispose() { Interlocked.Exchange(ref released, 1); } }
    private sealed class ScopeGuard : IDisposable { private int released; public bool IsReleased => Volatile.Read(ref released) != 0; public void Dispose() { Interlocked.Exchange(ref released, 1); } }
}
