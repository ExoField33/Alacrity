using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned main-thread queue. Integration calls <see cref="Drain"/> at its verified update boundary.</summary>
public sealed class PluginDispatcherHost
{
    private readonly object gate = new object();
    private readonly Queue<WorkItem> pending = new Queue<WorkItem>();
    private readonly int mainThreadId = Environment.CurrentManagedThreadId;

    public IPluginDispatcher CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new ScopedDispatcher(this, resources);
    }

    /// <summary>Runs queued work on the main thread. Cancelled scope-owned work is skipped.</summary>
    public void Drain(Action<Exception>? reportFailure = null)
    {
        if (Environment.CurrentManagedThreadId != mainThreadId)
            throw new InvalidOperationException("Plugin dispatcher can only drain on its owning main thread.");
        while (true)
        {
            WorkItem item;
            lock (gate)
            {
                if (pending.Count == 0) return;
                item = pending.Dequeue();
            }
            if (item.Registration.IsReleased) continue;
            try { item.Callback(); }
            catch (Exception exception) { reportFailure?.Invoke(exception); }
            finally { item.Registration.Dispose(); }
        }
    }

    private IPluginRegistration Post(IPluginResourceScope resources, Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        var registration = new WorkRegistration();
        resources.Own("dispatcher-work", PluginResourceKind.BackgroundTask, registration);
        lock (gate) pending.Enqueue(new WorkItem(callback, registration));
        return registration;
    }

    private sealed class ScopedDispatcher : IPluginDispatcher
    {
        private readonly PluginDispatcherHost host;
        private readonly IPluginResourceScope resources;
        public ScopedDispatcher(PluginDispatcherHost host, IPluginResourceScope resources) { this.host = host; this.resources = resources; }
        public bool IsMainThread => Environment.CurrentManagedThreadId == host.mainThreadId;
        public IPluginRegistration Post(Action callback) => host.Post(resources, callback);
    }

    private sealed class WorkItem { public WorkItem(Action callback, WorkRegistration registration) { Callback = callback; Registration = registration; } public Action Callback { get; } public WorkRegistration Registration { get; } }
    private sealed class WorkRegistration : IPluginRegistration { private bool released; public string Name => "dispatcher-work"; public bool IsReleased => released; public void Dispose() { released = true; } }
}
