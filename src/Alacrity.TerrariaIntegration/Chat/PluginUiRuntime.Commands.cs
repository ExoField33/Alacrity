using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria
{
    /// <summary>Local command dispatch forwarding for the version-locked chat hook.</summary>
    public static partial class PluginUiRuntime
    {
        /// <summary>Consumes only registered local plugin commands before Terraria creates an outgoing chat packet.</summary>
        public static bool TryHandlePluginChatCommand(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || text[0] != '/')
                    return false;
                BootstrapPluginRuntime();
                if (_commands == null)
                    return false;
                string[] parts = text.Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return false;
                var arguments = new string[Math.Max(0, parts.Length - 1)];
                if (arguments.Length > 0)
                    Array.Copy(parts, 1, arguments, 0, arguments.Length);
                return _commands.Dispatch(parts[0], arguments, ShowPluginCommandReply) != PluginCommandDispatchResult.NotFound;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Plugin chat command", exception);
                return false;
            }
        }

        private static void ShowPluginCommandReply(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Main.NewText(message, 190, 220, 255);
        }
    }
}
