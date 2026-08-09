using System;
using System.Collections.Generic;
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

                int commandStart = 1;
                int commandEnd = commandStart;
                while (commandEnd < text.Length && !char.IsWhiteSpace(text[commandEnd]))
                {
                    commandEnd++;
                }

                if (commandEnd == commandStart)
                {
                    return false;
                }

                string commandId = text.Substring(commandStart, commandEnd - commandStart);
                if (!_commands.IsRegistered(commandId))
                {
                    // Leave all unknown slash input, including malformed server commands, to vanilla.
                    return false;
                }

                if (!PluginCommandTokenizer.TryTokenize(text.Substring(1), out IReadOnlyList<string> parts, out string parseError))
                {
                    ShowPluginCommandReply(parseError!);
                    return true;
                }

                if (parts.Count == 0)
                    return false;

                var arguments = new string[Math.Max(0, parts.Count - 1)];
                if (arguments.Length > 0)
                {
                    for (int index = 0; index < arguments.Length; index++)
                    {
                        arguments[index] = parts[index + 1];
                    }
                }

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
