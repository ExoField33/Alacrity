using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria;

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
        if (pending.ContainsKey(id))
        {
            error = "Plugin operation is already in progress.";
            return false;
        }

        PluginPackageRuntimeRecord record = runtime.Registry.Records.Single(record => record.Manifest.Id == id);
        if (record.Controller == null || !record.Controller.UsesAsyncLifecycle)
        {
            if (enable)
                runtime.Enable(id);
            else
                runtime.Disable(id);
            persistEnabledState();
            return true;
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        Task task = enable ? runtime.EnableAsync(id, cancellation.Token) : runtime.DisableAsync(id, cancellation.Token);
        pending.Add(id, new PendingOperation(enable, task, cancellation));
        return true;
    }

    internal bool CompleteFinished()
    {
        bool changed = false;
        foreach (PluginId id in pending.Where(pair => pair.Value.Task.IsCompleted).Select(pair => pair.Key).ToArray())
        {
            PendingOperation operation = pending[id];
            pending.Remove(id);
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
        return pending.ContainsKey(id);
    }

    internal void CancelAll()
    {
        foreach (PendingOperation operation in pending.Values)
        {
            operation.Cancellation.Cancel();
            operation.Cancellation.Dispose();
        }
        pending.Clear();
    }

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
    }
}
