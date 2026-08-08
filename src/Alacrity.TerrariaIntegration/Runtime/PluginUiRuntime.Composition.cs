using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria
{
    /// <summary>
    /// Runtime composition, package discovery, state restoration, and operation coordination.
    /// It is intentionally separate from the version-locked patch ABI facade.
    /// </summary>
    public static partial class PluginUiRuntime
    {
        private static void EnsurePluginManager()
        {
            if (runtimeState != null)
                return;

            string root = AppDomain.CurrentDomain.BaseDirectory;
            runtimeState = PluginUiRuntimeState.Create(
                root,
                EnsureChatRuntime,
                GetActiveChatUserInteraction,
                ReportOptionalUiFailure,
                PersistEnabledPlugins,
                PublishPluginOperationNotification);
        }

        private static IPluginUserInteractionService GetActiveChatUserInteraction()
        {
            if (_chat == null || !_chat.TryGetActiveEditorInteraction(out IPluginUserInteractionService service) || service == null)
                return new PluginUserInteractionHost(UnsupportedPluginUserInteractionBackend.Instance).CreateService(new PluginManifest(new PluginId("alacrity.unavailable"), "Unavailable", new Version(1, 0), "Alacrity", "Unavailable", new[] { "1.4.5.6" }));
            return service;
        }

        private static void RefreshPluginCatalog()
        {
            _runtime.Discover(AppDomain.CurrentDomain.BaseDirectory);
            foreach (var record in _runtime.Registry.Records)
            {
                if (record.State != PluginPackageLifecycleState.Discovered || record.Manifest.EntryAssembly == null || record.Manifest.EntryType == null)
                    continue;

                try
                {
                    _runtime.LoadTrusted(record.Manifest.Id,
                        new PluginTrustVerificationResult(PluginTrustLevel.LocallyTrusted, "Locally installed package; cryptographic verification is not configured."),
                        new BridgePluginLogger(record.Manifest.Id),
                        new TerrariaMultiplayerSession());
                }
                catch (Exception exception)
                {
                    _runtime.Registry.MarkFaulted(record.Manifest.Id, exception.Message);
                }
            }
            RestoreEnabledPlugins();
        }

        private static void EnsureChatRuntime()
        {
            BootstrapPluginRuntime();
        }

        private static void RestoreEnabledPlugins()
        {
            _enabledStateStore?.RestoreOnce(_runtime, EnablePlugin, message => _notifications?.Publish(message, TimeSpan.FromSeconds(4)));
        }

        private static void EnablePlugin(PluginId id)
        {
            if (!BeginPluginOperation(id, enable: true, out string error))
            {
                _notifications?.Publish("Unable to enable " + id.Value + ": " + error, TimeSpan.FromSeconds(4));
            }
        }

        private static bool BeginPluginOperation(PluginId id, bool enable, out string error)
        {
            if (_pluginOperations == null)
            {
                error = "Plugin runtime is unavailable.";
                return false;
            }
            return _pluginOperations.Begin(id, enable, out error);
        }

        private static bool CompletePluginOperations()
        {
            return _pluginOperations != null && _pluginOperations.CompleteFinished();
        }

        private static void PublishPluginOperationNotification(string message, TimeSpan duration)
        {
            _notifications?.Publish(message, duration);
        }

        private static IEnumerable<PluginPackageRuntimeRecord> GetShutdownOrder()
        {
            if (_runtime == null) return Array.Empty<PluginPackageRuntimeRecord>();
            var records = _runtime.Registry.Records.ToDictionary(record => record.Manifest.Id);
            var visited = new HashSet<PluginId>();
            var dependencyFirst = new List<PluginPackageRuntimeRecord>();
            foreach (var record in records.Values)
                Visit(record);
            dependencyFirst.Reverse();
            return dependencyFirst;

            void Visit(PluginPackageRuntimeRecord record)
            {
                if (!visited.Add(record.Manifest.Id)) return;
                foreach (var dependency in record.Manifest.Dependencies)
                    if (records.TryGetValue(dependency.Id, out var dependencyRecord)) Visit(dependencyRecord);
                dependencyFirst.Add(record);
            }
        }

        private static void PersistEnabledPlugins()
        {
            _enabledStateStore?.Persist(_runtime, message => _notifications?.Publish(message, TimeSpan.FromSeconds(4)));
        }
    }
}
