using System;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
        /// <summary>Exact bridge ABI handshake consumed by the injected runtime before plugin bootstrap.</summary>
        /// <remarks>
        /// This entry point deliberately uses only locally compiled constants and BCL string formatting.
        /// A stale PluginSdk must not make the compatibility diagnostic itself uncallable.
        /// Integration tests assert these values remain synchronized with <see cref="AlacrityCompatibility"/>.
        /// </remarks>
        public static string GetBridgeHandshake() => "3|2|3|1.4.5.6";
        private sealed class BridgePluginLogger : IPluginLogger
        {
            private readonly PluginId plugin;
            public BridgePluginLogger(PluginId plugin) { this.plugin = plugin; }
            public void Debug(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Info(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Warn(string message) { System.Diagnostics.Trace.TraceWarning("[Alacrity:" + plugin + "] " + message); }
            public void Error(string message, Exception exception = null)
            {
                System.Diagnostics.Trace.TraceError("[Alacrity:" + plugin + "] " + message + (exception == null ? string.Empty : " " + exception));
            }
        }

        private sealed class TerrariaMultiplayerSession : IMultiplayerSession
        {
            public bool IsConnected { get { return Main.netMode == 1; } }
            public bool IsVanillaCompatibleMode { get { return true; } }
            public bool IsAlacrityAwareServer { get { return false; } }
            public ServerIdentity Server { get { return null; } }
            public ServerPluginPolicySnapshot ActivePolicy { get { return null; } }
        }
    }
}
