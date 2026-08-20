using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Terraria;

namespace AlacrityTerraria.Chat;

/// <summary>
/// Owns the generic chat-extension dispatch used by the version-locked bridge entry points.
/// It deliberately contains no bundled-plugin identity checks: enabled registrations decide
/// whether chat editing, decoration, filtering, links, or hover feedback are active.
/// </summary>
internal sealed class TerrariaPluginChatAdapter
{
    private static readonly ChatInputAction UpAction = new ChatInputAction("up");
    private static readonly ChatInputAction DownAction = new ChatInputAction("down");
    private readonly PluginChatHost chat;
    private readonly Action ensureRuntime;
    private readonly Func<IPluginUserInteractionService> activeEditorInteraction;
    private readonly Action<string, Exception> reportFailure;

    internal TerrariaPluginChatAdapter(
        PluginChatHost chat,
        Action ensureRuntime,
        Func<IPluginUserInteractionService> activeEditorInteraction,
        Action<string, Exception> reportFailure)
    {
        this.chat = chat ?? throw new ArgumentNullException(nameof(chat));
        this.ensureRuntime = ensureRuntime ?? throw new ArgumentNullException(nameof(ensureRuntime));
        this.activeEditorInteraction = activeEditorInteraction ?? throw new ArgumentNullException(nameof(activeEditorInteraction));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal bool HasInputEditors()
    {
        try
        {
            ensureRuntime();
            return chat.HasInputEditors;
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension activation", exception);
            return false;
        }
    }

    /// <summary>Checks whether a registered editor currently owns a native chat action.</summary>
    internal bool HasInputActionHandler(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
            return false;

        try
        {
            ensureRuntime();
            ChatInputAction action = actionId == "up"
                ? UpAction
                : actionId == "down"
                    ? DownAction
                    : new ChatInputAction(actionId);
            return chat.HasInputEditors && chat.HasInputActionHandler(action);
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension action ownership", exception);
            return false;
        }
    }

    /// <summary>Records a successfully submitted player-chat line after Terraria has normalized it.</summary>
    internal void RecordSubmittedInput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            ensureRuntime();
            if (chat.HasInputEditors)
                chat.Edit(new ChatInputSnapshot(text, text.Length, -1), new ChatInputAction("submit"));
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension submitted input", exception);
        }
    }

    internal string ProcessInput(string text, bool allowMultiLine)
    {
        try
        {
            ensureRuntime();
            chat.ObserveOutgoingInput(text);
            // The generic action menu owns wheel input while its chooser is open. Do this before
            // an editor can apply BetterChat's scroll action, because the menu is drawn later.
            TerrariaChatActionStrip.TryConsumeScrollWheel(chat);
            return chat.HasInputEditors
                ? TerrariaChatRuntime.Process(chat, activeEditorInteraction(), text, allowMultiLine)
                : text;
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension input", exception);
            return text;
        }
    }

    internal bool TryProcessActionStripInput()
    {
        try
        {
            ensureRuntime();
            if (TerrariaChatActionStrip.TryConsumeScrollWheel(chat))
            {
                return true;
            }

            return TerrariaChatActionStrip.TryProcessSearchInput(Main.inputText, Main.oldInputText);
        }
        catch (Exception exception)
        {
            reportFailure("Chat action-menu input", exception);
            return false;
        }
    }

    internal bool TryApplyInputAction(
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
        text = text ?? string.Empty;
        resultText = text;
        resultCaret = Math.Max(0, Math.Min(caret, text.Length));
        resultSelectionAnchor = selectionAnchor < 0 ? -1 : Math.Max(0, Math.Min(selectionAnchor, text.Length));
        appliedScrollLines = 0;
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        try
        {
            ensureRuntime();
            if (!chat.HasInputEditors)
            {
                return false;
            }

            var snapshot = new ChatInputSnapshot(text, resultCaret, resultSelectionAnchor);
            ChatInputEditResult result = chat.Edit(snapshot, new ChatInputAction(actionId, control, shift, null, scrollLines));
            if (!result.Handled)
            {
                return false;
            }

            resultText = result.Text;
            resultCaret = result.Caret;
            resultSelectionAnchor = result.SelectionAnchor;
            int boundedScrollLines = Math.Max(-TerrariaChatRuntime.MaximumChatScrollLinesPerAction, Math.Min(TerrariaChatRuntime.MaximumChatScrollLinesPerAction, result.ChatScrollLines));
            if (boundedScrollLines != 0)
            {
                Main.chatMonitor.Offset(boundedScrollLines);
                appliedScrollLines = boundedScrollLines;
            }

            return true;
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension normalized input action", exception);
            return false;
        }
    }

    internal bool TryHandleActionStripEscape()
    {
        try
        {
            ensureRuntime();
            return TerrariaChatActionStrip.TryHandleEscape();
        }
        catch (Exception exception)
        {
            reportFailure("Chat action-menu escape", exception);
            return false;
        }
    }

    internal string FormatInputForDraw(string text)
    {
        try
        {
            ensureRuntime();
            chat.ObserveOutgoingInput(text);
            return TerrariaChatRuntime.FormatForDraw(HasInputEditors(), text);
        }
        catch (Exception exception) { reportFailure("Chat extension draw text", exception); return text; }
    }

    /// <summary>Decorates a stored chat-monitor message. Editable input and unrelated UI text
    /// never enter this path.</summary>
    internal object DecorateStoredMessage(object snippets, Color baseColor, string originalMessage)
    {
        try
        {
            ensureRuntime();
            return chat.HasMessageDecorators
                ? TerrariaChatRuntime.DecorateStoredMessage(chat, snippets, baseColor, originalMessage)
                : snippets;
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension message decoration", exception);
            return snippets;
        }
    }

    /// <summary>Applies a reusable message-level presentation before Terraria performs its
    /// native word wrapping. This avoids treating each wrapped display fragment as a message.</summary>
    internal string PrepareStoredMessageText(object messageContainer, string originalMessage)
    {
        try
        {
            ensureRuntime();
            return chat.HasMessageDecorators
                ? TerrariaChatRuntime.PrepareStoredMessageText(chat, messageContainer, originalMessage)
                : originalMessage ?? string.Empty;
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension stored-message preparation", exception);
            return originalMessage ?? string.Empty;
        }
    }

    internal void BeginStoredMessageDecoration(object messageContainer)
    {
        TerrariaChatRuntime.BeginStoredMessageDecoration(messageContainer);
    }

    internal void EndStoredMessageDecoration()
    {
        TerrariaChatRuntime.EndStoredMessageDecoration();
    }

    internal void RefreshStoredMessagePresentations()
    {
        TerrariaChatRuntime.RefreshQueuedMessagePresentations();
    }

    internal bool ShouldDisplayNetworkMessage(byte messageAuthor)
    {
        try
        {
            ensureRuntime();
            return !chat.HasMessageFilters || chat.ShouldDisplay(
                messageAuthor == byte.MaxValue ? ChatMessageOrigin.Server : ChatMessageOrigin.Player);
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension network visibility", exception);
            return true;
        }
    }

    internal bool ShouldDisplayLocalMessage()
    {
        try
        {
            ensureRuntime();
            return !chat.HasMessageFilters || chat.ShouldDisplay(ChatMessageOrigin.LocalSystem);
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension local visibility", exception);
            return true;
        }
    }

    internal void HandleHover(object snippet)
    {
        try
        {
            ensureRuntime();
            if (chat.HasMessageDecorators || chat.HasInputEditors)
                TerrariaChatRuntime.Hover(snippet, chat);
        }
        catch (Exception exception) { reportFailure("Chat extension hover", exception); }
    }

    internal bool HandleClick(object snippet)
    {
        try
        {
            ensureRuntime();
            return (chat.HasLinkHandlers || chat.HasMessageActions) && TerrariaChatRuntime.Click(chat, snippet);
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension link activation", exception);
            return false;
        }
    }

    internal Color GetVisibleColor(object snippet, Color color)
    {
        try { return TerrariaChatRuntime.VisibleColor(snippet, color, chat); }
        catch (Exception exception) { reportFailure("Chat extension hover color", exception); return color; }
    }

    internal void CopySnippetContext(object source, object copy)
    {
        try { TerrariaChatRuntime.CopyContext(source, copy); }
        catch (Exception exception) { reportFailure("Chat extension snippet copy", exception); }
    }

    internal bool TryDeferOutgoingMessage(string text)
    {
        try
        {
            ensureRuntime();
            return chat.TryDeferOutgoingMessage(text);
        }
        catch (Exception exception)
        {
            reportFailure("Chat outgoing transform", exception);
            return false;
        }
    }

    internal bool TryTakeReadyOutgoingMessage(out string text)
    {
        try
        {
            ensureRuntime();
            return chat.TryTakeReadyOutgoingMessage(out text);
        }
        catch (Exception exception)
        {
            reportFailure("Chat outgoing transform completion", exception);
            text = string.Empty;
            return false;
        }
    }

    /// <summary>Stages a completed scoped transform before Terraria processes player chat this
    /// update. The normal native send path still owns packet creation and submission.</summary>
    internal void QueueReadyOutgoingMessageForNativeSubmit()
    {
        if (!Main.drawingPlayerChat)
        {
            return;
        }

        try
        {
            ensureRuntime();
            if (chat.TryTakeReadyOutgoingMessage(out string text))
            {
                Main.chatText = text;
                Main.inputTextEnter = true;
                Main.chatRelease = true;
                TerrariaChatRuntime.RequestReadyOutgoingSubmission();
            }
        }
        catch (Exception exception)
        {
            reportFailure("Chat outgoing transform handoff", exception);
        }
    }

    internal void DrawActionStrip()
    {
        try
        {
            ensureRuntime();
            TerrariaChatActionStrip.Draw(chat);
        }
        catch (Exception exception)
        {
            reportFailure("Chat action strip", exception);
        }
    }

    internal void ResetActionStrip()
    {
        TerrariaChatActionStrip.Reset();
    }
}
