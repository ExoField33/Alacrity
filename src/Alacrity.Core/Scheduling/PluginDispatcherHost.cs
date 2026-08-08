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
    private readonly Dictionary<PluginId, int> queuedByOwner = new Dictionary<PluginId, int>();
    private int queuedWorkCount;
    private readonly int mainThreadId = Environment.CurrentManagedThreadId;
    private readonly int maximumCallbacksPerDrain;
    private readonly TimeSpan maximumDrainTime;
    private readonly int maximumQueuedWork;
    private readonly int maximumQueuedWorkPerPlugin;

    public PluginDispatcherHost(int maximumCallbacksPerDrain = 64, TimeSpan? maximumDrainTime = null, int maximumQueuedWork = 512, int maximumQueuedWorkPerPlugin = 64)
    {
        if (maximumCallbacksPerDrain <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCallbacksPerDrain));
        this.maximumCallbacksPerDrain = maximumCallbacksPerDrain;
        this.maximumDrainTime = maximumDrainTime ?? TimeSpan.FromMilliseconds(2);
        if (this.maximumDrainTime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumDrainTime));
        if (maximumQueuedWork <= 0) throw new ArgumentOutOfRangeException(nameof(maximumQueuedWork));
        if (maximumQueuedWorkPerPlugin <= 0 || maximumQueuedWorkPerPlugin > maximumQueuedWork) throw new ArgumentOutOfRangeException(nameof(maximumQueuedWorkPerPlugin));
        this.maximumQueuedWork = maximumQueuedWork;
        this.maximumQueuedWorkPerPlugin = maximumQueuedWorkPerPlugin;
    }

    public IPluginDispatcher CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginLogger? logger = null)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        var guard = new ScopeGuard(this);
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
                ReleaseQueueSlotUnderLock(item.Owner);
            }
            if (item.Registration.IsReleased || item.Scope.IsReleased) continue;
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
        ReserveQueueSlot(owner);
        var registration = new WorkRegistration();
        try { resources.Own("dispatcher-work", PluginResourceKind.BackgroundTask, registration); }
        catch { registration.Dispose(); ReleaseQueueSlot(owner); throw; }
        lock (gate)
        {
            if (guard.IsReleased)
            {
                registration.Dispose();
                ReleaseQueueSlotUnderLock(owner);
                throw new ObjectDisposedException("IPluginDispatcher", "The owning plugin scope has been released.");
            }
            pending.Enqueue(new WorkItem(callback, registration, guard, owner, logger));
        }
        return registration;
    }

    private void ReserveQueueSlot(PluginId owner)
    {
        lock (gate)
        {
            if (queuedWorkCount >= maximumQueuedWork)
                throw new InvalidOperationException("The plugin dispatcher queue is full; work was rejected.");
            int owned = queuedByOwner.TryGetValue(owner, out int current) ? current : 0;
            if (owned >= maximumQueuedWorkPerPlugin)
                throw new InvalidOperationException("The plugin dispatcher queue limit was reached for '" + owner.Value + "'; work was rejected.");
            queuedByOwner[owner] = owned + 1;
            queuedWorkCount++;
        }
    }

    private void ReleaseQueueSlot(PluginId owner)
    {
        lock (gate)
            ReleaseQueueSlotUnderLock(owner);
    }

    private void ReleaseQueueSlotUnderLock(PluginId owner)
    {
        if (!queuedByOwner.TryGetValue(owner, out int current)) return;
        queuedWorkCount--;
        if (current <= 1) queuedByOwner.Remove(owner);
        else queuedByOwner[owner] = current - 1;
    }

    private void CancelAndRemove(ScopeGuard scope)
    {
        lock (gate)
        {
            if (pending.Count == 0) return;
            var retained = new Queue<WorkItem>(pending.Count);
            while (pending.Count != 0)
            {
                WorkItem item = pending.Dequeue();
                if (ReferenceEquals(item.Scope, scope))
                {
                    item.Registration.Dispose();
                    ReleaseQueueSlotUnderLock(item.Owner);
                }
                else retained.Enqueue(item);
            }
            while (retained.Count != 0) pending.Enqueue(retained.Dequeue());
        }
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

    private sealed class WorkItem { public WorkItem(Action callback, WorkRegistration registration, ScopeGuard scope, PluginId owner, IPluginLogger? logger) { Callback = callback; Registration = registration; Scope = scope; Owner = owner; Logger = logger; } public Action Callback { get; } public WorkRegistration Registration { get; } public ScopeGuard Scope { get; } public PluginId Owner { get; } public IPluginLogger? Logger { get; } public string Name => Registration.Name; }
    private sealed class WorkRegistration : IPluginRegistration { private int released; public string Name => "dispatcher-work"; public bool IsReleased => Volatile.Read(ref released) != 0; public void Dispose() { Interlocked.Exchange(ref released, 1); } }
    private sealed class ScopeGuard : IDisposable { private readonly PluginDispatcherHost host; private int released; public ScopeGuard(PluginDispatcherHost host) { this.host = host; } public bool IsReleased => Volatile.Read(ref released) != 0; public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) host.CancelAndRemove(this); } }
}
