using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
        /// <summary>Creates and starts the package runtime once during normal Terraria startup.</summary>
        public static void BootstrapPluginRuntime()
        {
            if (!RuntimeHost.TryBeginBootstrap())
            {
                return;
            }

            try
            {
                EnsurePluginManager();
                RefreshPluginCatalog();
                _extensions?.Publish(new ClientStartedEvent(TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)));
                RuntimeHost.CompleteBootstrap();
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin runtime bootstrap", exception);
            }
            finally
            {
                RuntimeHost.EndBootstrap();
            }
        }

        /// <summary>Best-effort process-exit cleanup. Individual plugin failures never block Terraria shutdown.</summary>
        public static void ShutdownPluginRuntime()
        {
            if (!RuntimeHost.TryBeginShutdown())
            {
                return;
            }

            // Do not hold the runtime host admission gate while publishing events, invoking lifecycle callbacks, or
            // coordinating workers: every one of those paths may execute plugin-controlled code.
            _extensions?.Publish(new ClientShuttingDownEvent(TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency)));
            _scheduler?.StopAcceptingWork();
            if (_pluginOperations != null)
            {
                ObserveShutdownTask(
                    "Plugin lifecycle operation shutdown",
                    _pluginOperations.CancelAllAsync(TimeSpan.FromSeconds(6)));
            }
            if (_runtime != null)
            {
                foreach (var record in GetShutdownOrder())
                {
                    if (_pluginOperations != null && _pluginOperations.IsPending(record.Manifest.Id))
                    {
                        ReportOptionalUiFailure("Plugin shutdown: " + record.Manifest.Id, new TimeoutException("A lifecycle operation did not stop before the shutdown timeout."));
                        continue;
                    }
                    try
                    {
                        if (record.Controller != null && record.Controller.UsesAsyncLifecycle)
                            BeginAsyncControllerShutdown(record.Manifest.Id, record.Controller);
                        else
                            record.Controller?.Dispose();
                    }
                    catch (Exception exception) { ReportOptionalUiFailure("Plugin shutdown: " + record.Manifest.Id, exception); }
                }
            }
            if (_scheduler != null)
            {
                ObserveShutdownTask(
                    "Plugin background shutdown",
                    _scheduler.CancelAndDrainBackgroundWorkAsync(TimeSpan.FromSeconds(3)));
            }
            _drawAdapter?.Dispose();
            _ingameBlankTexture?.Dispose();
            _ingameBlankTexture = null;
            _ingameBlankTextureDevice = null;
        }

        private static void BeginAsyncControllerShutdown(PluginId pluginId, PluginLifecycleController controller)
        {
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            try
            {
                _ = ObserveAsyncControllerShutdown(pluginId, controller.DisposeAsync(cancellation.Token), cancellation);
            }
            catch (Exception exception)
            {
                cancellation.Dispose();
                ReportOptionalUiFailure("Plugin shutdown: " + pluginId.Value, exception);
            }
        }

        private static async Task ObserveAsyncControllerShutdown(PluginId pluginId, Task shutdown, CancellationTokenSource cancellation)
        {
            try
            {
                await shutdown.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Trace.WriteLine("Alacrity async plugin shutdown failed for " + pluginId.Value + ": " + exception);
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private static void ObserveShutdownTask(string operation, Task<bool> task)
        {
            _ = ObserveShutdownTaskAsync(operation, task);
        }

        private static async Task ObserveShutdownTaskAsync(string operation, Task<bool> task)
        {
            try
            {
                if (!await task.ConfigureAwait(false))
                {
                    Trace.WriteLine("Alacrity " + operation + " exceeded its bounded cancellation timeout.");
                }
            }
            catch (Exception exception)
            {
                Trace.WriteLine("Alacrity " + operation + " failed: " + exception);
            }
        }
    }
}
