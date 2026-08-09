using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;

namespace AlacrityTerraria.Chat;

/// <summary>
/// Owns the generic chat-extension dispatch used by the version-locked bridge entry points.
/// It deliberately contains no bundled-plugin identity checks: enabled registrations decide
/// whether chat editing, decoration, filtering, links, or hover feedback are active.
/// </summary>
internal sealed class TerrariaPluginChatAdapter
{
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
            return chat.HasInputEditors && chat.HasInputActionHandler(new ChatInputAction(actionId));
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

    internal string FormatInputForDraw(string text)
    {
        try { return TerrariaChatRuntime.FormatForDraw(HasInputEditors(), text); }
        catch (Exception exception) { reportFailure("Chat extension draw text", exception); return text; }
    }

    internal object Decorate(object snippets, Color baseColor, string originalMessage)
    {
        try
        {
            ensureRuntime();
            return chat.HasMessageDecorators
                ? TerrariaChatRuntime.Decorate(chat, snippets, baseColor, originalMessage)
                : snippets;
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension message decoration", exception);
            return snippets;
        }
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
            return chat.HasLinkHandlers && TerrariaChatRuntime.Click(chat, snippet);
        }
        catch (Exception exception)
        {
            reportFailure("Chat extension link activation", exception);
            return false;
        }
    }

    internal Color GetVisibleColor(object snippet, Color color)
    {
        try { return TerrariaChatRuntime.VisibleColor(snippet, color); }
        catch (Exception exception) { reportFailure("Chat extension hover color", exception); return color; }
    }

    internal void CopySnippetContext(object source, object copy)
    {
        try { TerrariaChatRuntime.CopyContext(source, copy); }
        catch (Exception exception) { reportFailure("Chat extension snippet copy", exception); }
    }
}
