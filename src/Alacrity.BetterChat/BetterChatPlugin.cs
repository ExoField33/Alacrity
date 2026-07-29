using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Alacrity.PluginSdk;

namespace Alacrity.BetterChat;

/// <summary>Vanilla-compatible chat editing and link presentation plugin.</summary>
public sealed class BetterChatPlugin : IAlacrityPlugin
{
    private IPluginContext? context;

    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        InitializeSetting("clickableLinks", true);
        Set("visibility", GetVisibility());

        context.Ui.RegisterSettingsPage(new PluginUiContribution("better-chat", "Better Chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("clickable-links", "Clickable Links", () => Get("clickableLinks", true), value => Set("clickableLinks", value)));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle("visibility", "Chat Visibility", new[] { "Enabled", "Disabled" }, GetVisibility, value => Set("visibility", value)));

        context.Terraria.Chat.RegisterInputEditor(new ChatInputEditorDescriptor("better-chat-editor"), new Editor(this));
        context.Terraria.Chat.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("better-chat-links"), new LinkDecorator(this));
        context.Terraria.Chat.RegisterMessageFilter(new ChatMessageFilterDescriptor("better-chat-visibility"), new VisibilityFilter(this));
        context.Terraria.Chat.RegisterLinkHandler(new ChatLinkHandlerDescriptor(Uri.UriSchemeHttp), new LinkHandler(this));
        context.Terraria.Chat.RegisterLinkHandler(new ChatLinkHandlerDescriptor(Uri.UriSchemeHttps), new LinkHandler(this));
    }

    public void Enable() { }
    public void Disable() { }
    public void Shutdown() { context = null; }

    private bool Get(string key, bool defaultValue) => context != null && context.Settings.Get(key, defaultValue);
    private string Get(string key, string defaultValue) => context != null ? context.Settings.Get(key, defaultValue) : defaultValue;
    private void Set(string key, bool value) { if (context != null) context.Settings.Set(key, value); }
    private void Set(string key, string value) { if (context != null) context.Settings.Set(key, value); }
    private void InitializeSetting(string key, bool defaultValue) { if (context != null) context.Settings.Set(key, context.Settings.Get(key, defaultValue)); }
    private string GetVisibility() => string.Equals(Get("visibility", "Enabled"), "Disabled", StringComparison.Ordinal) ? "Disabled" : "Enabled";
    private bool TryOpenExternalLink(Uri uri) => context != null && context.UserInteraction.TryOpenExternalLink(uri);

    private sealed class VisibilityFilter : IChatMessageFilter
    {
        private readonly BetterChatPlugin plugin;
        public VisibilityFilter(BetterChatPlugin plugin) { this.plugin = plugin; }
        public bool ShouldDisplay(ChatMessageOrigin origin)
        {
            string visibility = plugin.GetVisibility();
            return !string.Equals(visibility, "Disabled", StringComparison.Ordinal);
        }
    }

    private sealed class Editor : IChatInputEditor
    {
        private readonly BetterChatPlugin plugin;
        public Editor(BetterChatPlugin plugin) { this.plugin = plugin; }
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
        private static readonly Regex Url = new Regex(@"https?://[^\s\]\)\""']+|www\.[^\s\]\)\""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly BetterChatPlugin plugin;
        public LinkDecorator(BetterChatPlugin plugin) { this.plugin = plugin; }
        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message)
        {
            if (!plugin.Get("clickableLinks", true)) return new[] { new ChatTextSpan(message.Text) };
            var spans = new List<ChatTextSpan>();
            int index = 0;
            foreach (Match match in Url.Matches(message.Text))
            {
                if (match.Index > index) spans.Add(new ChatTextSpan(message.Text.Substring(index, match.Index - index)));
                string value = TrimTrailingPunctuation(match.Value);
                if (!TryHttpUri(value, out string target))
                    spans.Add(new ChatTextSpan(match.Value));
                else
                    spans.Add(new ChatTextSpan(value, target));
                int consumed = match.Index + value.Length;
                if (consumed < match.Index + match.Length) spans.Add(new ChatTextSpan(message.Text.Substring(consumed, match.Index + match.Length - consumed)));
                index = match.Index + match.Length;
            }
            if (index < message.Text.Length || spans.Count == 0) spans.Add(new ChatTextSpan(message.Text.Substring(index)));
            return spans;
        }

        private static string TrimTrailingPunctuation(string value)
        {
            while (!string.IsNullOrEmpty(value) && ".,;!?:".IndexOf(value[value.Length - 1]) >= 0)
                value = value.Substring(0, value.Length - 1);
            if (value.EndsWith(")", StringComparison.Ordinal) && value.Count(character => character == '(') < value.Count(character => character == ')'))
                value = value.Substring(0, value.Length - 1);
            return value;
        }
        private static bool TryHttpUri(string value, out string target)
        {
            target = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;
            return Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
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
