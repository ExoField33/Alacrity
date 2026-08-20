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
            return _chatAdapter != null && (_chatAdapter.HasInputEditors() || Chat.TerrariaChatActionStrip.IsOpen);
        }

        /// <summary>Returns whether a generic editor currently owns a native chat-navigation action.</summary>
        public static bool ShouldHandleChatInputAction(string actionId)
        {
            return _chatAdapter != null && _chatAdapter.HasInputActionHandler(actionId);
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

        /// <summary>Lets an open host-owned action chooser consume search input before native text editing.</summary>
        public static bool TryProcessChatActionInput()
        {
            return _chatAdapter != null && _chatAdapter.TryProcessActionStripInput();
        }

        /// <summary>Lets an open host-owned action chooser consume Escape before player chat closes.</summary>
        public static bool TryHandleChatActionEscape()
        {
            return _chatAdapter != null && _chatAdapter.TryHandleActionStripEscape();
        }

        /// <summary>
        /// Applies one normalized non-text player-chat action through activation-scoped editors.
        /// The native editor owns typing, clipboard, and selection; this narrow bridge preserves
        /// extensible history and scroll behavior without reprocessing raw keyboard input.
        /// </summary>
        public static bool TryApplyChatInputAction(
            string text,
            int caret,
            int selectionAnchor,
            string actionId,
            bool control,
            bool shift,
            int scrollLines,
            out string resultText,
            out int resultCaret,
            out int resultSelectionAnchor,
            out int appliedScrollLines)
        {
            if (_chatAdapter == null)
            {
                resultText = text ?? string.Empty;
                resultCaret = caret;
                resultSelectionAnchor = selectionAnchor;
                appliedScrollLines = 0;
                return false;
            }

            return _chatAdapter.TryApplyInputAction(
                text,
                caret,
                selectionAnchor,
                actionId,
                control,
                shift,
                scrollLines,
                out resultText,
                out resultCaret,
                out resultSelectionAnchor,
                out appliedScrollLines);
        }

        /// <summary>Notifies generic editors after Terraria has accepted a non-empty player-chat submission.</summary>
        public static void RecordSubmittedChatInput(string text)
        {
            _chatAdapter?.RecordSubmittedInput(text);
        }

        /// <summary>Defers an eligible outgoing chat line while a scoped transformer prepares it.</summary>
        public static bool TryDeferOutgoingChatMessage(string text)
        {
            return _chatAdapter != null && _chatAdapter.TryDeferOutgoingMessage(text);
        }

        /// <summary>Returns a completed outgoing replacement that Terraria should submit normally.</summary>
        public static string TakeReadyOutgoingChatMessage()
        {
            return _chatAdapter != null && _chatAdapter.TryTakeReadyOutgoingMessage(out string text) ? text : null;
        }

        /// <summary>Returns whether a completed outgoing replacement is waiting for Terraria's
        /// native player-chat submit gate.</summary>
        public static bool HasReadyOutgoingChatMessage()
        {
            return TerrariaChatRuntime.HasReadyOutgoingSubmission();
        }

        /// <summary>Draws host-owned generic chat action buttons after Terraria has drawn chat text.</summary>
        public static void DrawChatActionStrip()
        {
            _chatAdapter?.DrawActionStrip();
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

        /// <summary>Decorates one stored chat-monitor message without affecting editable player
        /// input or arbitrary UI text parsed by Terraria.</summary>
        public static object DecorateStoredChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            return _chatAdapter == null ? snippets : _chatAdapter.DecorateStoredMessage(snippets, baseColor, originalMessage);
        }

        /// <summary>Prepares one complete retained message before the native chat monitor wraps
        /// it into display snippets. Plugins only receive detached text and a host-owned handle.</summary>
        public static string PrepareStoredChatMessageText(string originalMessage, object messageContainer)
        {
            return _chatAdapter == null
                ? originalMessage ?? string.Empty
                : _chatAdapter.PrepareStoredMessageText(messageContainer, originalMessage);
        }

        /// <summary>Begins the short-lived chat-monitor parse scope used by the version-locked
        /// layout hook. Other ChatManager parsing, including editable input, remains untouched.</summary>
        public static void BeginStoredChatMessageDecoration()
        {
            _chatAdapter?.BeginStoredMessageDecoration(null!);
        }

        /// <summary>Begins a native retained-chat parse and associates new snippets with their
        /// owning container so a scoped presentation update can request Terraria's normal rewrap.</summary>
        public static void BeginStoredChatMessageDecorationForContainer(object messageContainer)
        {
            _chatAdapter?.BeginStoredMessageDecoration(messageContainer);
        }

        /// <summary>Ends the current stored chat-monitor parse scope.</summary>
        public static void EndStoredChatMessageDecoration()
        {
            _chatAdapter?.EndStoredMessageDecoration();
        }

        /// <summary>Marks retained native chat containers changed by scoped presentation work
        /// before Terraria updates and redraws them.</summary>
        public static void RefreshStoredChatMessagePresentations()
        {
            _chatAdapter?.RefreshStoredMessagePresentations();
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
