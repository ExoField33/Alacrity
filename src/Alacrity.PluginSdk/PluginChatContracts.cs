using System;
using System.Collections.Generic;

#pragma warning disable CS1591 // Detailed member docs are added with the public SDK guide in this migration slice.

namespace Alacrity.PluginSdk;

/// <summary>Host-provided Terraria services. New Terraria capabilities belong here rather than expanding <see cref="IPluginContext"/>.</summary>
public interface ITerrariaServices
{
    /// <summary>Player-chat editing and presentation services available on supported Terraria versions.</summary>
    IPluginChatService Chat { get; }
}

/// <summary>Registers bounded player-chat extensions. The host owns Terraria hooks and rendering internals.</summary>
public interface IPluginChatService
{
    /// <summary>Registers a player-chat editor. Editors run in ascending priority order until one handles an action.</summary>
    IPluginRegistration RegisterInputEditor(ChatInputEditorDescriptor descriptor, IChatInputEditor editor);

    /// <summary>Registers a message decorator that returns immutable presentation spans.</summary>
    IPluginRegistration RegisterMessageDecorator(ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator);

    /// <summary>Registers a display filter for classified incoming or local chat messages.</summary>
    IPluginRegistration RegisterMessageFilter(ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter);

    /// <summary>Registers an external-link activation handler for a declared URI scheme.</summary>
    IPluginRegistration RegisterLinkHandler(ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler);
}

/// <summary>Immutable player-chat text, caret, and selection snapshot.</summary>
public sealed class ChatInputSnapshot
{
    public ChatInputSnapshot(string text, int caret, int selectionAnchor)
    {
        Text = text ?? string.Empty;
        Caret = Math.Max(0, Math.Min(caret, Text.Length));
        SelectionAnchor = selectionAnchor < 0 ? -1 : Math.Max(0, Math.Min(selectionAnchor, Text.Length));
    }
    public string Text { get; }
    public int Caret { get; }
    public int SelectionAnchor { get; }
}

/// <summary>Normalized host input action. Plugins never receive raw keyboard state.</summary>
public sealed class ChatInputAction
{
    public ChatInputAction(string id, bool control = false, bool shift = false, string? text = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("An input action ID is required.", nameof(id)) : id;
        Control = control;
        Shift = shift;
        Text = text;
    }
    public string Id { get; }
    public bool Control { get; }
    public bool Shift { get; }
    public string? Text { get; }
}

/// <summary>Replacement state returned by a chat editor.</summary>
public sealed class ChatInputEditResult
{
    public ChatInputEditResult(string text, int caret, int selectionAnchor, bool handled)
    {
        Text = text ?? string.Empty;
        Caret = Math.Max(0, Math.Min(caret, Text.Length));
        SelectionAnchor = selectionAnchor < 0 ? -1 : Math.Max(0, Math.Min(selectionAnchor, Text.Length));
        Handled = handled;
    }
    public string Text { get; }
    public int Caret { get; }
    public int SelectionAnchor { get; }
    public bool Handled { get; }
    public static ChatInputEditResult Unhandled(ChatInputSnapshot snapshot) => new ChatInputEditResult(snapshot.Text, snapshot.Caret, snapshot.SelectionAnchor, false);
}

public sealed class ChatInputEditorDescriptor
{
    public ChatInputEditorDescriptor(string id, int priority = 0)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat editor ID is required.", nameof(id)) : id;
        Priority = priority;
    }
    public string Id { get; }
    public int Priority { get; }
}

public interface IChatInputEditor { ChatInputEditResult Edit(ChatInputSnapshot snapshot, ChatInputAction action); }

public sealed class ChatMessageSnapshot
{
    public ChatMessageSnapshot(string text) => Text = text ?? string.Empty;
    public string Text { get; }
}

public sealed class ChatTextSpan
{
    public ChatTextSpan(string text, string? linkTarget = null) { Text = text ?? string.Empty; LinkTarget = linkTarget; }
    public string Text { get; }
    public string? LinkTarget { get; }
}

public sealed class ChatMessageDecoratorDescriptor
{
    public ChatMessageDecoratorDescriptor(string id, int priority = 0) { Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat decorator ID is required.", nameof(id)) : id; Priority = priority; }
    public string Id { get; }
    public int Priority { get; }
}

public interface IChatMessageDecorator { IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message); }

/// <summary>Origin assigned by the host before chat is converted to display text.</summary>
public enum ChatMessageOrigin
{
    Player,
    Server,
    LocalSystem
}

/// <summary>Stable declaration for a chat display filter.</summary>
public sealed class ChatMessageFilterDescriptor
{
    public ChatMessageFilterDescriptor(string id, int priority = 0)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat filter ID is required.", nameof(id)) : id;
        Priority = priority;
    }

    public string Id { get; }
    public int Priority { get; }
}

/// <summary>Decides whether a host-classified message should reach Terraria's chat display.</summary>
public interface IChatMessageFilter { bool ShouldDisplay(ChatMessageOrigin origin); }

public sealed class ChatLinkHandlerDescriptor
{
    public ChatLinkHandlerDescriptor(string scheme) { Scheme = string.IsNullOrWhiteSpace(scheme) ? throw new ArgumentException("A URI scheme is required.", nameof(scheme)) : scheme; }
    public string Scheme { get; }
}

public interface IChatLinkHandler { bool TryActivate(Uri uri); }

#pragma warning restore CS1591
