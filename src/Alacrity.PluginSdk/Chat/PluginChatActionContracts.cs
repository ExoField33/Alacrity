using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// <summary>Stable host-generated identity for one rendered chat text segment.</summary>
public readonly struct ChatMessageHandle : IEquatable<ChatMessageHandle>
{
    /// <summary>Creates a non-zero chat presentation identity.</summary>
    public ChatMessageHandle(long value)
    {
        Value = value;
    }

    /// <summary>Monotonically assigned host identity. It is valid only for the current chat session.</summary>
    public long Value { get; }

    /// <summary>Whether this is a host-assigned identity.</summary>
    public bool IsValid => Value != 0;

    /// <inheritdoc />
    public bool Equals(ChatMessageHandle other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ChatMessageHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Compares two message identities.</summary>
    public static bool operator ==(ChatMessageHandle left, ChatMessageHandle right) => left.Equals(right);

    /// <summary>Compares two message identities.</summary>
    public static bool operator !=(ChatMessageHandle left, ChatMessageHandle right) => !left.Equals(right);
}

/// <summary>Stable declaration for a scoped text-span action.</summary>
public sealed class ChatMessageActionDescriptor
{
    /// <summary>Creates a message action declaration.</summary>
    public ChatMessageActionDescriptor(string id)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat message action ID is required.", nameof(id)) : id;
    }

    /// <summary>Owner-local action identifier referenced by chat spans.</summary>
    public string Id { get; }
}

/// <summary>Host-normalized invocation of a plugin-owned chat text-span action.</summary>
public sealed class ChatMessageActionInvocation
{
    /// <summary>Creates an immutable action invocation.</summary>
    public ChatMessageActionInvocation(ChatMessageHandle message, string target, bool shift)
    {
        Message = message;
        Target = target ?? string.Empty;
        Shift = shift;
    }

    /// <summary>Host identity of the rendered message segment.</summary>
    public ChatMessageHandle Message { get; }

    /// <summary>Plugin-defined opaque target retained in the span.</summary>
    public string Target { get; }

    /// <summary>Whether Shift was held during the click.</summary>
    public bool Shift { get; }
}

/// <summary>Handles a click on a span owned by the active plugin activation.</summary>
public interface IChatMessageActionHandler
{
    /// <summary>Handles one host-normalized click. It executes on Terraria's chat/UI thread.</summary>
    bool TryActivate(ChatMessageActionInvocation invocation);
}

/// <summary>Immutable replacement presentation for a chat segment. The number of spans must match
/// the original decorated segment so the host can update Terraria snippets without rebuilding live
/// chat-monitor state.</summary>
public sealed class ChatMessagePresentation
{
    /// <summary>Creates a replacement presentation from the supplied ordered spans.</summary>
    public ChatMessagePresentation(IReadOnlyList<ChatTextSpan> spans)
    {
        if (spans == null || spans.Count == 0)
        {
            throw new ArgumentException("A replacement chat presentation needs at least one span.", nameof(spans));
        }

        var copy = new ChatTextSpan[spans.Count];
        for (int index = 0; index < spans.Count; index++)
        {
            copy[index] = spans[index] ?? throw new ArgumentException("Chat presentation spans cannot contain null values.", nameof(spans));
        }

        Spans = copy;
    }

    /// <summary>Ordered detached spans replacing the original segment.</summary>
    public IReadOnlyList<ChatTextSpan> Spans { get; }
}

/// <summary>Host-rendered action button placed in Terraria's chat action strip.</summary>
public sealed class ChatActionButtonDescriptor
{
    /// <summary>Creates an immutable chat action button declaration.</summary>
    public ChatActionButtonDescriptor(string id, string assetPath, int priority = 0, PluginTooltipOptions? tooltip = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat action button ID is required.", nameof(id)) : id;
        AssetPath = string.IsNullOrWhiteSpace(assetPath) || assetPath.IndexOf("..", StringComparison.Ordinal) >= 0
            ? throw new ArgumentException("A package-relative asset path is required.", nameof(assetPath))
            : assetPath.Replace('\\', '/');
        Priority = priority;
        Tooltip = tooltip;
    }

    /// <summary>Owner-local stable identifier.</summary>
    public string Id { get; }

    /// <summary>Plugin-package-relative compiled texture path without a file extension.</summary>
    public string AssetPath { get; }

    /// <summary>Ascending display order within the host-owned action strip.</summary>
    public int Priority { get; }

    /// <summary>Optional tooltip used while the icon is hovered.</summary>
    public PluginTooltipOptions? Tooltip { get; }
}

/// <summary>Detached host snapshot used to render one registered chat action button.</summary>
public sealed class ChatActionButtonView
{
    /// <summary>Creates a host-owned action-button view.</summary>
    public ChatActionButtonView(PluginId owner, ChatActionButtonDescriptor descriptor)
    {
        Owner = owner;
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    /// <summary>Plugin activation that owns this button.</summary>
    public PluginId Owner { get; }

    /// <summary>Immutable rendering and placement declaration.</summary>
    public ChatActionButtonDescriptor Descriptor { get; }
}

/// <summary>Pointer button that activated a host-rendered chat action.</summary>
public enum ChatActionButtonMouseButton
{
    /// <summary>The primary (left) pointer button.</summary>
    Left,

    /// <summary>The secondary (right) pointer button.</summary>
    Right
}

/// <summary>Host-normalized button invocation. The normal left click opens menu items when
/// supplied; plugins can opt into modifier-based quick actions without receiving raw input.</summary>
public sealed class ChatActionButtonInvocation
{
    /// <summary>Creates a left-button invocation for existing action handlers.</summary>
    public ChatActionButtonInvocation(bool shift)
        : this(ChatActionButtonMouseButton.Left, shift)
    {
    }

    /// <summary>Creates an immutable button invocation.</summary>
    public ChatActionButtonInvocation(ChatActionButtonMouseButton button, bool shift)
    {
        Button = button;
        Shift = shift;
    }

    /// <summary>Pointer button that activated the action.</summary>
    public ChatActionButtonMouseButton Button { get; }

    /// <summary>Whether Shift was held for the click.</summary>
    public bool Shift { get; }
}

/// <summary>Optional background request rendered by the host behind a chat action icon.</summary>
public readonly struct ChatActionButtonVisualState
{
    /// <summary>Creates a solid or split button background. Supplying both colors produces a
    /// host-rendered left-to-right blend without giving the plugin renderer access.</summary>
    public ChatActionButtonVisualState(PluginColor? primaryBackground, PluginColor? secondaryBackground = null)
    {
        PrimaryBackground = primaryBackground;
        SecondaryBackground = secondaryBackground;
    }

    /// <summary>Primary background color, or null for the native unaccented appearance.</summary>
    public PluginColor? PrimaryBackground { get; }

    /// <summary>Optional second background color blended across the opposite side of the icon.</summary>
    public PluginColor? SecondaryBackground { get; }

    /// <summary>Compatibility alias for code compiled against the initial action-strip contract.</summary>
    [Obsolete("Use PrimaryBackground. Chat action accents now fill the button background.")]
    public PluginColor? PrimaryBorder => PrimaryBackground;

    /// <summary>Compatibility alias for code compiled against the initial action-strip contract.</summary>
    [Obsolete("Use SecondaryBackground. Chat action accents now fill the button background.")]
    public PluginColor? SecondaryBorder => SecondaryBackground;
}

/// <summary>Preferred direction for one nested host-owned chat action chooser.</summary>
public enum ChatActionMenuDirection
{
    /// <summary>Places the search field above choices and lets choices extend downward.</summary>
    Down,

    /// <summary>Places choices above a search field anchored at the chooser's bottom edge.</summary>
    Up
}

/// <summary>One host-rendered menu row attached to a chat action button.</summary>
public sealed class ChatActionMenuItem
{
    /// <summary>Creates an actionable menu row with an optional current value.</summary>
    public ChatActionMenuItem(string id, string label, string? value = null, bool enabled = true)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat action menu item ID is required.", nameof(id)) : id;
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("A chat action menu label is required.", nameof(label)) : label;
        Value = value;
        Enabled = enabled;
        Children = Array.Empty<ChatActionMenuItem>();
    }

    /// <summary>Creates a menu row that opens a host-owned nested chooser.</summary>
    public ChatActionMenuItem(
        string id,
        string label,
        IReadOnlyList<ChatActionMenuItem> children,
        string? value = null,
        bool enabled = true,
        ChatActionMenuDirection childMenuDirection = ChatActionMenuDirection.Down)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A chat action menu item ID is required.", nameof(id)) : id;
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("A chat action menu label is required.", nameof(label)) : label;
        if (children == null || children.Count == 0)
        {
            throw new ArgumentException("A nested chat menu needs at least one child item.", nameof(children));
        }

        if (childMenuDirection != ChatActionMenuDirection.Down && childMenuDirection != ChatActionMenuDirection.Up)
        {
            throw new ArgumentOutOfRangeException(nameof(childMenuDirection));
        }

        var copy = new ChatActionMenuItem[children.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < children.Count; index++)
        {
            ChatActionMenuItem child = children[index] ?? throw new ArgumentException("Nested chat menu items cannot contain null values.", nameof(children));
            if (!ids.Add(child.Id))
            {
                throw new ArgumentException("Nested chat menu item IDs must be unique within one menu.", nameof(children));
            }

            copy[index] = child;
        }

        Value = value;
        Enabled = enabled;
        Children = copy;
        ChildMenuDirection = childMenuDirection;
    }

    /// <summary>Owner-local stable menu item identifier.</summary>
    public string Id { get; }

    /// <summary>Visible row label.</summary>
    public string Label { get; }

    /// <summary>Optional right-aligned current value.</summary>
    public string? Value { get; }

    /// <summary>Whether the row currently accepts activation.</summary>
    public bool Enabled { get; }

    /// <summary>Nested host-rendered choices. A non-empty collection means clicking this row opens
    /// the nested chooser rather than invoking the plugin callback.</summary>
    public IReadOnlyList<ChatActionMenuItem> Children { get; }

    /// <summary>Requested layout direction for this item's nested chooser. This affects only
    /// host placement and never exposes raw UI state to a plugin.</summary>
    public ChatActionMenuDirection ChildMenuDirection { get; }

    /// <summary>Whether this row opens a nested chooser.</summary>
    public bool HasChildren => Children.Count != 0;
}

/// <summary>Plugin callback for a host-rendered chat action button and its side popover.</summary>
public interface IChatActionButtonHandler
{
    /// <summary>Handles an icon click. It executes on Terraria's chat/UI thread.</summary>
    void Activate(ChatActionButtonInvocation invocation);

    /// <summary>Builds menu rows only while the host popover is open.</summary>
    IReadOnlyList<ChatActionMenuItem> GetMenuItems();

    /// <summary>Handles an enabled menu-row click on Terraria's chat/UI thread.</summary>
    void ActivateMenuItem(string id);

    /// <summary>Resolves the current optional accent border while the chat icon is visible.</summary>
    ChatActionButtonVisualState GetVisualState();
}

/// <summary>Stable declaration for a host-owned outgoing-chat transformer.</summary>
public sealed class ChatOutgoingMessageTransformerDescriptor
{
    /// <summary>Creates an outgoing transformer declaration.</summary>
    public ChatOutgoingMessageTransformerDescriptor(string id, int priority = 0)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("An outgoing chat transformer ID is required.", nameof(id)) : id;
        Priority = priority;
    }

    /// <summary>Owner-local transformer identifier.</summary>
    public string Id { get; }

    /// <summary>Ascending transformer precedence. The first transformer that claims a message owns it.</summary>
    public int Priority { get; }
}

/// <summary>Detached player-authored text awaiting normal Terraria chat submission.</summary>
public sealed class ChatOutgoingMessageSnapshot
{
    /// <summary>Creates an immutable outgoing message snapshot.</summary>
    public ChatOutgoingMessageSnapshot(string text)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>Raw player-authored input after Terraria normalization.</summary>
    public string Text { get; }

    /// <summary>Whether the text is a command and must normally retain native command behavior.</summary>
    public bool IsCommand => Text.Length > 0 && Text[0] == '/';
}

/// <summary>Result of an asynchronously prepared outgoing message.</summary>
public sealed class ChatOutgoingMessageTransformResult
{
    private ChatOutgoingMessageTransformResult(bool success, string text, string? diagnostic)
    {
        Success = success;
        Text = text ?? string.Empty;
        Diagnostic = diagnostic;
    }

    /// <summary>Whether the host should submit the transformed text through Terraria's normal path.</summary>
    public bool Success { get; }

    /// <summary>Replacement text when <see cref="Success"/> is true.</summary>
    public string Text { get; }

    /// <summary>Safe diagnostic shown only for a failed transform.</summary>
    public string? Diagnostic { get; }

    /// <summary>Creates a successful replacement result.</summary>
    public static ChatOutgoingMessageTransformResult Replace(string text) => new ChatOutgoingMessageTransformResult(true, text, null);

    /// <summary>Creates a failure result that leaves the original text unsent in the input box.</summary>
    public static ChatOutgoingMessageTransformResult Fail(string? diagnostic = null) => new ChatOutgoingMessageTransformResult(false, string.Empty, diagnostic);
}

/// <summary>Transforms selected player-authored messages off the Terraria main thread.</summary>
public interface IChatOutgoingMessageTransformer
{
    /// <summary>Returns whether this transformer owns the detached outgoing message.</summary>
    bool CanTransform(ChatOutgoingMessageSnapshot message);

    /// <summary>Performs asynchronous work without touching live Terraria state.</summary>
    Task<ChatOutgoingMessageTransformResult> TransformAsync(ChatOutgoingMessageSnapshot message, CancellationToken cancellationToken);
}
