using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Alacrity.PluginSdk;

namespace Alacrity.BetterChat;

/// <summary>Vanilla-compatible chat editing and link presentation plugin.</summary>
public sealed class BetterChatPlugin : IAlacrityPlugin
{
    private IPluginContext? context;
    private bool clickableLinks = true;
    private bool chatVisibility = true;

    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        clickableLinks = context.Settings.Get("clickableLinks", true);
        chatVisibility = ReadChatVisibility(context.Settings);

        context.Ui.RegisterSettingsPage(new PluginUiContribution("better-chat", "Better Chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("clickable-links", "Clickable Links", () => clickableLinks, SetClickableLinks));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("chat-visibility", "Chat Visibility", () => chatVisibility, SetChatVisibility));

        context.Terraria.Chat.RegisterInputEditor(new ChatInputEditorDescriptor("better-chat-editor"), new Editor());
        context.Terraria.Chat.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("better-chat-links"), new LinkDecorator(this));
        context.Terraria.Chat.RegisterMessageFilter(new ChatMessageFilterDescriptor("better-chat-visibility"), new VisibilityFilter(this));
        context.Terraria.Chat.RegisterLinkHandler(new ChatLinkHandlerDescriptor(Uri.UriSchemeHttp), new LinkHandler(this));
        context.Terraria.Chat.RegisterLinkHandler(new ChatLinkHandlerDescriptor(Uri.UriSchemeHttps), new LinkHandler(this));
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown() { context = null; }

    private void SetClickableLinks(bool value)
    {
        if (clickableLinks == value) return;
        clickableLinks = value;
        context?.Settings.Set("clickableLinks", value);
    }

    private static bool ReadChatVisibility(IPluginSettings settings)
    {
        bool? current = settings.Get<bool?>("chat-visibility", null);
        if (current.HasValue)
            return current.Value;

        // `visibility` was the initial string setting. Retain its user choice once, then persist
        // only the stable boolean setting used by the toggle.
        string? legacy = settings.Get<string?>("visibility", null);
        if (legacy == null)
            return true;
        bool migrated = !string.Equals(legacy, "Disabled", StringComparison.Ordinal);
        settings.Set("chat-visibility", migrated);
        settings.Remove("visibility");
        return migrated;
    }

    private void SetChatVisibility(bool value)
    {
        if (chatVisibility == value) return;
        chatVisibility = value;
        context?.Settings.Set("chat-visibility", value);
    }

    private bool TryOpenExternalLink(Uri uri) => context != null && context.UserInteraction.TryOpenExternalLink(uri);

    private sealed class VisibilityFilter : IChatMessageFilter
    {
        private readonly BetterChatPlugin plugin;
        public VisibilityFilter(BetterChatPlugin plugin) { this.plugin = plugin; }
        public bool ShouldDisplay(ChatMessageOrigin origin)
        {
            return plugin.chatVisibility;
        }
    }

    private sealed class Editor : IChatInputEditor
    {
        public ChatInputEditResult Edit(ChatInputSnapshot snapshot, ChatInputAction action)
        {
            int caret = snapshot.Caret;
            int anchor = snapshot.SelectionAnchor;
            bool selecting = action.Shift;
            if (!selecting) anchor = -1;
            if (selecting && anchor < 0) anchor = caret;
            switch (action.Id)
            {
                case "left": caret = action.Control ? PreviousWord(snapshot.Text, caret) : PreviousUnit(snapshot.Text, caret); break;
                case "right": caret = action.Control ? NextWord(snapshot.Text, caret) : NextUnit(snapshot.Text, caret); break;
                case "home": caret = 0; break;
                case "end": caret = snapshot.Text.Length; break;
                default: return ChatInputEditResult.Unhandled(snapshot);
            }
            return new ChatInputEditResult(snapshot.Text, caret, anchor, true);
        }
        private static int PreviousWord(string text, int index) { while (index > 0 && char.IsWhiteSpace(text, PreviousUnit(text, index))) index = PreviousUnit(text, index); while (index > 0 && !char.IsWhiteSpace(text, PreviousUnit(text, index))) index = PreviousUnit(text, index); return index; }
        private static int NextWord(string text, int index) { while (index < text.Length && !char.IsWhiteSpace(text, index)) index = NextUnit(text, index); while (index < text.Length && char.IsWhiteSpace(text, index)) index = NextUnit(text, index); return index; }
        private static int PreviousUnit(string text, int index)
        {
            if (TryGetChatTagAt(text, Math.Max(0, index - 1), out int start, out _)) return start;
            return PreviousScalar(text, index);
        }
        private static int NextUnit(string text, int index)
        {
            if (TryGetChatTagAt(text, Math.Min(index, text.Length - 1), out _, out int end)) return end;
            return NextScalar(text, index);
        }
        private static bool TryGetChatTagAt(string text, int index, out int start, out int end)
        {
            start = end = -1;
            if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length) return false;
            int opening = text.LastIndexOf('[', index);
            if (opening < 0 || opening + 3 >= text.Length) return false;
            char kind = text[opening + 1];
            char tagSyntax = text[opening + 2];
            if ((kind != 'i' && kind != 'g') || (tagSyntax != ':' && !(kind == 'i' && tagSyntax == '/'))) return false;
            int closing = text.IndexOf(']', opening + 3);
            if (closing < 0 || index > closing) return false;
            start = opening;
            end = closing + 1;
            return true;
        }
        private static int PreviousScalar(string text, int index) => index > 1 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2]) ? index - 2 : Math.Max(0, index - 1);
        private static int NextScalar(string text, int index) => index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? index + 2 : Math.Min(text.Length, index + 1);
    }

    private sealed class LinkDecorator : IChatMessageDecorator
    {
        private readonly BetterChatPlugin plugin;
        public LinkDecorator(BetterChatPlugin plugin) { this.plugin = plugin; }
        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message)
        {
            return plugin.clickableLinks ? BetterChatUrlParser.Decorate(message.Text) : new[] { new ChatTextSpan(message.Text) };
        }
    }

    private sealed class LinkHandler : IChatLinkHandler
    {
        private readonly BetterChatPlugin plugin;
        public LinkHandler(BetterChatPlugin plugin) { this.plugin = plugin; }
        public bool TryActivate(Uri uri)
        {
            if (uri == null || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;
            return plugin.TryOpenExternalLink(uri);
        }
    }
}

internal static class BetterChatUrlParser
{
    private static readonly Regex Url = new Regex(@"https?://[^\s\]\""']+|www\.[^\s\]\""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static IReadOnlyList<ChatTextSpan> Decorate(string? message)
    {
        string text = message ?? string.Empty;
        var spans = new List<ChatTextSpan>();
        int index = 0;
        foreach (Match match in Url.Matches(text))
        {
            if (match.Index > index) spans.Add(new ChatTextSpan(text.Substring(index, match.Index - index)));
            string value = TrimTrailingPunctuation(match.Value);
            if (TryHttpUri(value, out string target)) spans.Add(new ChatTextSpan(value, target));
            else spans.Add(new ChatTextSpan(value));
            int consumed = match.Index + value.Length;
            if (consumed < match.Index + match.Length) spans.Add(new ChatTextSpan(text.Substring(consumed, match.Index + match.Length - consumed)));
            index = match.Index + match.Length;
        }
        if (index < text.Length || spans.Count == 0) spans.Add(new ChatTextSpan(text.Substring(index)));
        return spans;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        while (!string.IsNullOrEmpty(value) && ".,;!?:".IndexOf(value[value.Length - 1]) >= 0)
            value = value.Substring(0, value.Length - 1);
        while (value.EndsWith(")", StringComparison.Ordinal) && Count(value, '(') < Count(value, ')'))
            value = value.Substring(0, value.Length - 1);
        return value;
    }

    private static int Count(string value, char character)
    {
        int count = 0;
        for (int index = 0; index < value.Length; index++) if (value[index] == character) count++;
        return count;
    }

    private static bool TryHttpUri(string value, out string target)
    {
        target = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;
        return Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
