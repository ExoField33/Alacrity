using System;
using System.Collections.Generic;

#pragma warning disable CS1591 // Detailed member docs are added with the public SDK guide in this migration slice.

namespace Alacrity.PluginSdk;

/// Host-provided Terraria services. New Terraria capabilities belong here rather than expanding <see cref="IPluginContext"/>.
public interface ITerrariaServices
{
    /// Player-chat editing and presentation services available on supported Terraria versions.
    IPluginChatService Chat { get; }

    /// Read-only, allocation-conscious entity snapshots supplied by the active Terraria integration.
    IPluginEntitySnapshotService Entities { get; }
    /// Read-only player names, status, and buffs from the shared integration snapshot cache.
    IPluginPlayerService Players { get; }
    /// Scoped policy registrations for optional client-side visual effects.
    IPluginVisualEffectsService VisualEffects { get; }
    /// Read-only world/server presentation data such as the display name and sampled ping.
    IPluginSessionPresentationService Session { get; }
    /// Demand-gated hostile NPC-to-player targeting relationships for presentation diagnostics.
    IPluginNpcTargetSnapshotService NpcTargets { get; }
    /// Bounded visible client tile-section state for world diagnostics.
    IPluginWorldSectionService WorldSections { get; }
}

/// Registers bounded player-chat extensions. The host owns Terraria hooks and rendering internals.
public interface IPluginChatService
{
    /// Registers a player-chat editor. Editors run in ascending priority order until one handles an action.
    IPluginRegistration RegisterInputEditor(ChatInputEditorDescriptor descriptor, IChatInputEditor editor);

    /// Registers a message decorator that returns immutable presentation spans.
    IPluginRegistration RegisterMessageDecorator(ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator);

    /// Registers a display filter for classified incoming or local chat messages.
    IPluginRegistration RegisterMessageFilter(ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter);

    /// Registers an external-link activation handler for a declared URI scheme.
    IPluginRegistration RegisterLinkHandler(ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler);
}

/// Immutable player-chat text, caret, and selection snapshot.
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

/// Normalized host input action. Plugins never receive raw keyboard state.
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

/// Replacement state returned by a chat editor.
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
    /// <summary>Host-assigned owner of this presentation span. Plugin-supplied values are ignored by the host.</summary>
    public ChatTextSpan(string text, string? linkTarget, PluginId owner) { Text = text ?? string.Empty; LinkTarget = linkTarget; Owner = owner; }
    public string Text { get; }
    public string? LinkTarget { get; }
    public PluginId Owner { get; }
}

public sealed class ChatMessageDecoratorDescriptor
{
    public ChatMessageDecoratorDescriptor(string id, int priority = 0) { Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat decorator ID is required.", nameof(id)) : id; Priority = priority; }
    public string Id { get; }
    public int Priority { get; }
}

public interface IChatMessageDecorator { IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message); }

/// 
/// Optional composable chat decorator. Implement this interface when a decorator needs to preserve
/// spans produced by earlier registrations, including their host-validated link targets.
/// 
public interface IChatSpanDecorator : IChatMessageDecorator
{
    /// Transforms the current ordered spans without exposing Terraria chat objects.
    IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot originalMessage, IReadOnlyList<ChatTextSpan> currentSpans);
}

/// Origin assigned by the host before chat is converted to display text.
public enum ChatMessageOrigin
{
    Player,
    Server,
    LocalSystem
}

/// Stable declaration for a chat display filter.
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

/// Decides whether a host-classified message should reach Terraria's chat display.
public interface IChatMessageFilter { bool ShouldDisplay(ChatMessageOrigin origin); }

public sealed class ChatLinkHandlerDescriptor
{
    public ChatLinkHandlerDescriptor(string scheme) { Scheme = string.IsNullOrWhiteSpace(scheme) ? throw new ArgumentException("A URI scheme is required.", nameof(scheme)) : scheme; }
    public string Scheme { get; }
}

public interface IChatLinkHandler { bool TryActivate(Uri uri); }

#pragma warning restore CS1591
