using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Alacrity.PluginSdk;

namespace Alacrity.BetterChat;

/// <summary>Vanilla-compatible chat editing and link presentation plugin.</summary>
public sealed class BetterChatPlugin : IAlacrityPlugin
{
    private IPluginContext? context;
    private IPluginSetting<bool>? clickableLinksSetting;
    private IPluginSetting<bool>? chatVisibilitySetting;
    private IPluginSetting<bool>? scrollChatSetting;
    private IPluginSetting<int>? scrollChatSensitivitySetting;
    private IPluginSetting<bool>? chatHistorySetting;
    private bool clickableLinks = true;
    private bool chatVisibility = true;
    private bool scrollChat = true;
    private int scrollChatSensitivity = 1;
    private bool chatHistory = true;
    private Editor? editor;

    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        ReadChatVisibility(context.Settings);
        clickableLinksSetting = context.Settings.Register(new PluginSettingDefinition<bool>("clickableLinks", true));
        chatVisibilitySetting = context.Settings.Register(new PluginSettingDefinition<bool>("chat-visibility", true));
        scrollChatSetting = context.Settings.Register(new PluginSettingDefinition<bool>("scrollChat", true));
        scrollChatSensitivitySetting = context.Settings.Register(new PluginSettingDefinition<int>("scrollChatSensitivity", 1, value => Clamp(value, 1, 4)));
        chatHistorySetting = context.Settings.Register(new PluginSettingDefinition<bool>("chatHistory", true));
        clickableLinks = clickableLinksSetting.Value;
        chatVisibility = chatVisibilitySetting.Value;
        scrollChat = scrollChatSetting.Value;
        scrollChatSensitivity = scrollChatSensitivitySetting.Value;
        chatHistory = chatHistorySetting.Value;
        clickableLinksSetting.Subscribe(value => clickableLinks = value);
        chatVisibilitySetting.Subscribe(value => chatVisibility = value);
        scrollChatSetting.Subscribe(value => scrollChat = value);
        scrollChatSensitivitySetting.Subscribe(value => scrollChatSensitivity = value);
        chatHistorySetting.Subscribe(value => chatHistory = value);

        context.Ui.RegisterSettingsPage(new PluginUiContribution("better-chat", "Better Chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("clickable-links", "Clickable Links", clickableLinksSetting).InPage("better-chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("chat-visibility", "Chat Visibility", chatVisibilitySetting).InPage("better-chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("scroll-chat", "Scroll Chat", scrollChatSetting).InPage("better-chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("scroll-chat-sensitivity", "Scroll Sensitivity", 1f, 4f, 1f, scrollChatSensitivitySetting, FormatScrollSensitivity).InPage("better-chat"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("chat-history", "Chat History", chatHistorySetting).InPage("better-chat"));

        editor = new Editor(this);
        context.Terraria.Chat.RegisterInputEditor(new ChatInputEditorDescriptor("better-chat-editor"), editor);
        context.Terraria.Chat.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("better-chat-links"), new LinkDecorator(this));
        context.Terraria.Chat.RegisterMessageFilter(new ChatMessageFilterDescriptor("better-chat-visibility"), new VisibilityFilter(this));
        context.Terraria.Chat.RegisterLinkHandler(new ChatLinkHandlerDescriptor(Uri.UriSchemeHttp), new LinkHandler(this));
        context.Terraria.Chat.RegisterLinkHandler(new ChatLinkHandlerDescriptor(Uri.UriSchemeHttps), new LinkHandler(this));
        context.Events.Subscribe<ClientMenuStateChangedEvent>(OnClientMenuStateChanged);
    }

    public void Enable() { }
    public void Disable() { editor?.ClearHistory(); }
    public void Shutdown()
    {
        editor?.ClearHistory();
        editor = null;
        clickableLinksSetting = null;
        chatVisibilitySetting = null;
        scrollChatSetting = null;
        scrollChatSensitivitySetting = null;
        chatHistorySetting = null;
        context = null;
    }

    private void OnClientMenuStateChanged(ClientMenuStateChangedEvent change)
    {
        // BetterChat history belongs to the current world/server visit and must not survive a return to menus.
        if (change.IsGameMenu)
            editor?.ClearHistory();
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

    private bool TryOpenExternalLink(Uri uri) => context != null && context.UserInteraction.TryOpenExternalLink(uri);

    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    private static string FormatScrollSensitivity(float value)
    {
        return ((int)Math.Round(value)).ToString();
    }

    private sealed class VisibilityFilter : IChatMessageFilter
    {
        private readonly BetterChatPlugin plugin;
        public VisibilityFilter(BetterChatPlugin plugin) { this.plugin = plugin; }
        public bool ShouldDisplay(ChatMessageOrigin origin)
        {
            return plugin.chatVisibility;
        }
    }

    private sealed class Editor : IChatInputEditor, IChatInputActionAvailability
    {
        private const int MaximumHistoryEntries = 200;
        private readonly BetterChatPlugin plugin;
        private readonly List<string> history = new List<string>();
        private int historyIndex = -1;
        private string historyDraft = string.Empty;

        public Editor(BetterChatPlugin plugin)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        }

        public bool CanHandle(ChatInputAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            switch (action.Id)
            {
                case "up":
                case "down":
                    return plugin.chatHistory;
                case "scroll":
                    return plugin.scrollChat;
                default:
                    return false;
            }
        }

        public ChatInputEditResult Edit(ChatInputSnapshot snapshot, ChatInputAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            if (action.Id == "submit")
            {
                RecordHistory(snapshot.Text);
                return ChatInputEditResult.Unhandled(snapshot);
            }

            if (action.Id == "scroll")
            {
                if (!plugin.scrollChat || action.ScrollLines == 0)
                    return ChatInputEditResult.Unhandled(snapshot);

                int lines = action.ScrollLines * plugin.scrollChatSensitivity;
                return new ChatInputEditResult(snapshot.Text, snapshot.Caret, snapshot.SelectionAnchor, true, lines);
            }

            if (action.Id == "up" || action.Id == "down")
            {
                if (plugin.chatHistory)
                    return NavigateHistory(snapshot, action.Id == "up");

                return ChatInputEditResult.Unhandled(snapshot);
            }

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

        internal void ClearHistory()
        {
            history.Clear();
            historyIndex = -1;
            historyDraft = string.Empty;
        }

        private void RecordHistory(string text)
        {
            if (!plugin.chatHistory || string.IsNullOrWhiteSpace(text))
                return;

            history.Add(text);
            if (history.Count > MaximumHistoryEntries)
            {
                history.RemoveAt(0);
            }

            historyIndex = -1;
            historyDraft = string.Empty;
        }

        private ChatInputEditResult NavigateHistory(ChatInputSnapshot snapshot, bool previous)
        {
            if (historyIndex >= 0 && !string.Equals(snapshot.Text, history[historyIndex], StringComparison.Ordinal))
            {
                historyIndex = -1;
                historyDraft = snapshot.Text;
            }

            if (history.Count == 0)
                return new ChatInputEditResult(snapshot.Text, snapshot.Caret, snapshot.SelectionAnchor, true);

            if (previous)
            {
                if (historyIndex < 0)
                {
                    historyDraft = snapshot.Text;
                    historyIndex = history.Count - 1;
                }
                else if (historyIndex > 0)
                {
                    historyIndex--;
                }
            }
            else
            {
                if (historyIndex < 0)
                    return new ChatInputEditResult(snapshot.Text, snapshot.Caret, snapshot.SelectionAnchor, true);

                if (historyIndex < history.Count - 1)
                {
                    historyIndex++;
                }
                else
                {
                    historyIndex = -1;
                    return new ChatInputEditResult(historyDraft, historyDraft.Length, -1, true);
                }
            }

            string entry = history[historyIndex];
            return new ChatInputEditResult(entry, entry.Length, -1, true);
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

    private sealed class LinkDecorator : IChatSpanDecorator
    {
        private readonly BetterChatPlugin plugin;
        public LinkDecorator(BetterChatPlugin plugin) { this.plugin = plugin; }
        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message)
        {
            return plugin.clickableLinks ? BetterChatUrlParser.Decorate(message.Text) : new[] { new ChatTextSpan(message.Text) };
        }

        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot originalMessage, IReadOnlyList<ChatTextSpan> currentSpans)
        {
            if (!plugin.clickableLinks || currentSpans.Count == 0)
                return currentSpans;

            var result = new List<ChatTextSpan>(currentSpans.Count);
            for (int index = 0; index < currentSpans.Count; index++)
            {
                ChatTextSpan span = currentSpans[index];
                if (span.LinkTarget != null)
                {
                    result.Add(span);
                    continue;
                }

                IReadOnlyList<ChatTextSpan> decorated = BetterChatUrlParser.Decorate(span.Text);
                for (int decoratedIndex = 0; decoratedIndex < decorated.Count; decoratedIndex++)
                    result.Add(decorated[decoratedIndex]);
            }
            return result;
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
