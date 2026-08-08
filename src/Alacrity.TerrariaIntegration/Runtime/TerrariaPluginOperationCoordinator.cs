using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.Runtime;

/// <summary>
/// Coordinates lifecycle actions initiated by Terraria UI. Work is started at the UI boundary and
/// completed by polling; the update path never blocks waiting for an asynchronous plugin callback.
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
            if (stopping)
            {
                error = "Plugin runtime is shutting down.";
                return false;
            }

            if (pending.ContainsKey(id))
            {
                error = "Plugin operation is already in progress.";
                return false;
            }
        }

        PluginPackageRuntimeRecord record = runtime.Registry.Records.Single(record => record.Manifest.Id == id);
        if (record.Controller == null || !record.Controller.UsesAsyncLifecycle)
        {
            lock (gate)
            {
                if (stopping || pending.ContainsKey(id))
                {
                    error = stopping ? "Plugin runtime is shutting down." : "Plugin operation is already in progress.";
                    return false;
                }

                pending.Add(id, PendingOperation.Synchronous(enable));
            }
            try
            {
                if (enable) runtime.Enable(id); else runtime.Disable(id);
                persistEnabledState();
                return true;
            }
            finally
            {
                lock (gate)
                {
                    pending.Remove(id);
                }
            }
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        Task task = enable ? runtime.EnableAsync(id, cancellation.Token) : runtime.DisableAsync(id, cancellation.Token);
        lock (gate)
        {
            if (stopping)
            {
                cancellation.Cancel();
                Observe(task);
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
        List<KeyValuePair<PluginId, PendingOperation>> completed = null;
        lock (gate)
        {
            foreach (KeyValuePair<PluginId, PendingOperation> pair in pending)
            {
                if (pair.Value.Task == null || !pair.Value.Task.IsCompleted)
                {
                    continue;
                }

                completed ??= new List<KeyValuePair<PluginId, PendingOperation>>();
                completed.Add(pair);
            }
        }

        if (completed == null)
        {
            return false;
        }

        for (int index = 0; index < completed.Count; index++)
        {
            PluginId id = completed[index].Key;
            PendingOperation operation = completed[index].Value;
            lock (gate)
            {
                if (!pending.TryGetValue(id, out PendingOperation current) || !ReferenceEquals(current, operation)) continue;
                pending.Remove(id);
            }
            operation.Cancellation.Dispose();
            try
            {
                if (operation.Task.IsCanceled)
                {
                    throw new OperationCanceledException("The plugin lifecycle operation was cancelled.");
                }

                if (operation.Task.IsFaulted)
                {
                    throw operation.Task.Exception.GetBaseException();
                }

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
        Observe(CancelAllAsync(TimeSpan.Zero));
    }

    /// <summary>
    /// Stops admission and cancels UI-started async lifecycle operations. The caller observes the
    /// bounded task instead of blocking Terraria's update or render thread waiting for a worker.
    /// </summary>
    internal Task<bool> CancelAllAsync(TimeSpan timeout)
    {
        PendingOperation[] operations;
        lock (gate)
        {
            stopping = true;
            operations = pending.Values.ToArray();
        }
        var asynchronous = new List<PendingOperation>();
        for (int index = 0; index < operations.Length; index++)
        {
            if (operations[index].Task != null)
            {
                asynchronous.Add(operations[index]);
            }
        }

        for (int index = 0; index < asynchronous.Count; index++)
        {
            asynchronous[index].Cancellation.Cancel();
        }

        return CompleteCancellationAsync(asynchronous.ToArray(), timeout);
    }

    private async Task<bool> CompleteCancellationAsync(PendingOperation[] asynchronous, TimeSpan timeout)
    {
        if (asynchronous.Length == 0)
        {
            RemoveCompletedOperations();
            return true;
        }

        Task[] tasks = new Task[asynchronous.Length];
        for (int index = 0; index < asynchronous.Length; index++)
        {
            tasks[index] = asynchronous[index].Task;
        }

        Task all = Task.WhenAll(tasks);
        bool completed = all.IsCompleted;
        if (!completed && timeout > TimeSpan.Zero)
        {
            using (var timeoutCancellation = new CancellationTokenSource())
            {
                Task timeoutTask = Task.Delay(timeout, timeoutCancellation.Token);
                completed = await Task.WhenAny(all, timeoutTask).ConfigureAwait(false) == all;
                timeoutCancellation.Cancel();
            }
        }

        if (completed)
        {
            Observe(all);
            RemoveCompletedOperations();
            return true;
        }

        for (int index = 0; index < asynchronous.Length; index++)
        {
            PendingOperation operation = asynchronous[index];
            Observe(operation.Task);
            _ = operation.Task.ContinueWith(
                _ => RemoveCompletedOperation(operation),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return false;
    }

    private void RemoveCompletedOperations()
    {
        lock (gate)
        {
            foreach (KeyValuePair<PluginId, PendingOperation> pair in pending.ToArray())
            {
                PendingOperation operation = pair.Value;
                if (operation.Task != null && !operation.Task.IsCompleted)
                {
                    continue;
                }

                pending.Remove(pair.Key);
                operation.Cancellation?.Dispose();
            }
        }
    }

    private void RemoveCompletedOperation(PendingOperation operation)
    {
        lock (gate)
        {
            PluginId? matchingId = null;
            foreach (KeyValuePair<PluginId, PendingOperation> pair in pending)
            {
                if (!ReferenceEquals(pair.Value, operation))
                {
                    continue;
                }

                matchingId = pair.Key;
                break;
            }

            if (matchingId.HasValue)
            {
                pending.Remove(matchingId.Value);
                operation.Cancellation?.Dispose();
            }
        }
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
