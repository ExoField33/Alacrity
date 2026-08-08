using Microsoft.Xna.Framework;

namespace AlacrityTerraria
{
    /// <summary>
    /// Patch-facing chat ABI forwarding. The injected Terraria methods continue to call
    /// <see cref="PluginUiRuntime"/>, while generic chat dispatch remains in
    /// <see cref="TerrariaPluginChatAdapter"/>.
    /// </summary>
    public static partial class PluginUiRuntime
    {
        /// <summary>Legacy ABI name retained for the currently patched executable.</summary>
        public static bool IsBetterChatActive()
        {
            return HasChatInputEditors();
        }

        /// <summary>Returns whether any enabled plugin has registered a chat input editor.</summary>
        public static bool HasChatInputEditors()
        {
            return _chatAdapter != null && _chatAdapter.HasInputEditors();
        }

        /// <summary>Legacy ABI name retained for focused player-chat input.</summary>
        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            return ProcessChatInput(text, allowMultiLine);
        }

        /// <summary>Processes player-chat input through the generic scoped editor pipeline.</summary>
        public static string ProcessChatInput(string text, bool allowMultiLine)
        {
            return _chatAdapter == null ? text : _chatAdapter.ProcessInput(text, allowMultiLine);
        }

        /// <summary>Legacy ABI name retained for draw-only chat formatting.</summary>
        public static string FormatPlayerChatText(string text)
        {
            return FormatChatInputForDraw(text);
        }

        /// <summary>Formats focused chat text without changing outgoing packet text.</summary>
        public static string FormatChatInputForDraw(string text)
        {
            return _chatAdapter == null ? text : _chatAdapter.FormatInputForDraw(text);
        }

        /// <summary>Decorates parsed normal-chat snippets outside Terraria's hot draw path.</summary>
        public static object DecorateChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            return _chatAdapter == null ? snippets : _chatAdapter.Decorate(snippets, baseColor, originalMessage);
        }

        /// <summary>Filters network chat before Terraria publishes it to local presentation.</summary>
        public static bool ShouldDisplayNetworkChatMessage(byte messageAuthor)
        {
            return _chatAdapter == null || _chatAdapter.ShouldDisplayNetworkMessage(messageAuthor);
        }

        /// <summary>Filters client-originated local system messages.</summary>
        public static bool ShouldDisplayLocalChatMessage()
        {
            return _chatAdapter == null || _chatAdapter.ShouldDisplayLocalMessage();
        }

        /// <summary>Provides host-owned hover feedback and copy handling for an interactive span.</summary>
        public static void HandleChatSnippetHover(object snippet)
        {
            if (_chatAdapter != null)
                _chatAdapter.HandleHover(snippet);
        }

        /// <summary>Attempts to activate an owner-scoped validated external link.</summary>
        public static bool HandleChatSnippetClick(object snippet)
        {
            return _chatAdapter != null && _chatAdapter.HandleClick(snippet);
        }

        /// <summary>Applies hover color without mutating Terraria's original snippet color.</summary>
        public static Color GetChatSnippetVisibleColor(object snippet, Color color)
        {
            return _chatAdapter == null ? color : _chatAdapter.GetVisibleColor(snippet, color);
        }

        /// <summary>Transfers parse-time span ownership when Terraria clones a snippet for layout.</summary>
        public static void CopyChatSnippetContext(object source, object copy)
        {
            if (_chatAdapter != null)
                _chatAdapter.CopySnippetContext(source, copy);
        }
    }
}
