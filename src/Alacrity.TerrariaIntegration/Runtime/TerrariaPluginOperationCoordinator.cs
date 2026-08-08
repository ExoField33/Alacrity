using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.Runtime;

/// <summary>
/// Coordinates lifecycle actions initiated by Terraria UI. Synchronous plugins stay synchronous;
/// asynchronous plugins are polled from the UI update path so their callbacks never hold a menu
/// click handler hostage.
/// </summary>
internal sealed class TerrariaPluginOperationCoordinator
{
    private readonly PluginManagerRuntime runtime;
    private readonly Action persistEnabledState;
    private readonly Action<string, TimeSpan> notify;
    private readonly Dictionary<PluginId, PendingOperation> pending = new Dictionary<PluginId, PendingOperation>();
    private readonly object gate = new object();
    private bool stopping;

    internal TerrariaPluginOperationCoordinator(
        PluginManagerRuntime runtime,
        Action persistEnabledState,
        Action<string, TimeSpan> notify)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.persistEnabledState = persistEnabledState ?? throw new ArgumentNullException(nameof(persistEnabledState));
        this.notify = notify ?? throw new ArgumentNullException(nameof(notify));
    }

    internal bool Begin(PluginId id, bool enable, out string error)
    {
        error = string.Empty;
        lock (gate)
        {
            if (stopping) { error = "Plugin runtime is shutting down."; return false; }
            if (pending.ContainsKey(id)) { error = "Plugin operation is already in progress."; return false; }
        }

        PluginPackageRuntimeRecord record = runtime.Registry.Records.Single(record => record.Manifest.Id == id);
        if (record.Controller == null || !record.Controller.UsesAsyncLifecycle)
        {
            lock (gate)
            {
                if (stopping || pending.ContainsKey(id)) { error = stopping ? "Plugin runtime is shutting down." : "Plugin operation is already in progress."; return false; }
                pending.Add(id, PendingOperation.Synchronous(enable));
            }
            try
            {
                if (enable) runtime.Enable(id); else runtime.Disable(id);
                persistEnabledState();
                return true;
            }
            finally { lock (gate) pending.Remove(id); }
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        Task task = enable ? runtime.EnableAsync(id, cancellation.Token) : runtime.DisableAsync(id, cancellation.Token);
        lock (gate)
        {
            if (stopping)
            {
                cancellation.Cancel();
                Observe(task);
                cancellation.Dispose();
                error = "Plugin runtime is shutting down.";
                return false;
            }
            pending.Add(id, new PendingOperation(enable, task, cancellation));
        }
        return true;
    }

    internal bool CompleteFinished()
    {
        bool changed = false;
        KeyValuePair<PluginId, PendingOperation>[] completed;
        lock (gate)
            completed = pending.Where(pair => pair.Value.Task != null && pair.Value.Task.IsCompleted).ToArray();
        foreach (KeyValuePair<PluginId, PendingOperation> pair in completed)
        {
            PluginId id = pair.Key;
            PendingOperation operation = pair.Value;
            lock (gate)
            {
                if (!pending.TryGetValue(id, out PendingOperation current) || !ReferenceEquals(current, operation)) continue;
                pending.Remove(id);
            }
            operation.Cancellation.Dispose();
            try
            {
            operation.Task.GetAwaiter().GetResult();
                persistEnabledState();
                notify((operation.Enable ? "Enabled " : "Disabled ") + id.Value + ".", TimeSpan.FromSeconds(4));
            }
            catch (Exception exception)
            {
                notify("Unable to " + (operation.Enable ? "enable " : "disable ") + id.Value + ": " + exception.Message, TimeSpan.FromSeconds(4));
            }
            changed = true;
        }
        return changed;
    }

    internal bool IsPending(PluginId id)
    {
        lock (gate) return pending.ContainsKey(id);
    }

    internal void CancelAll()
    {
        CancelAllAndWait(TimeSpan.Zero);
    }

    internal bool CancelAllAndWait(TimeSpan timeout)
    {
        PendingOperation[] operations;
        lock (gate)
        {
            stopping = true;
            operations = pending.Values.ToArray();
        }
        PendingOperation[] asynchronous = operations.Where(operation => operation.Task != null).ToArray();
        for (int index = 0; index < asynchronous.Length; index++) asynchronous[index].Cancellation.Cancel();
        bool allCompleted = asynchronous.Length == 0;
        if (asynchronous.Length != 0 && timeout > TimeSpan.Zero)
        {
            Task all = Task.WhenAll(asynchronous.Select(operation => operation.Task));
            try { Task.WhenAny(all, Task.Delay(timeout)).GetAwaiter().GetResult(); }
            catch { }
            allCompleted = all.IsCompleted;
        }
        lock (gate)
        {
            foreach (KeyValuePair<PluginId, PendingOperation> pair in pending.ToArray())
            {
                PendingOperation operation = pair.Value;
                if (operation.Task == null || operation.Task.IsCompleted)
                {
                    if (operation.Task != null) Observe(operation.Task);
                    operation.Cancellation?.Dispose();
                    pending.Remove(pair.Key);
                }
                else
                {
                    Observe(operation.Task);
                    CancellationTokenSource cancellation = operation.Cancellation;
                    operation.Task.ContinueWith(_ => cancellation.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
                }
            }
        }
        return allCompleted;
    }

    private static void Observe(Task task) { task.ContinueWith(completed => { _ = completed.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously); }

    private sealed class PendingOperation
    {
        internal PendingOperation(bool enable, Task task, CancellationTokenSource cancellation)
        {
            Enable = enable;
            Task = task;
            Cancellation = cancellation;
        }

        internal bool Enable { get; }
        internal Task Task { get; }
        internal CancellationTokenSource Cancellation { get; }
        internal static PendingOperation Synchronous(bool enable) => new PendingOperation(enable, null, null);
    }
}
