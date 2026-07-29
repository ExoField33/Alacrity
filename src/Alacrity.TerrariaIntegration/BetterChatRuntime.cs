using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.UI.Chat;

namespace AlacrityTerraria
{
    // This is deliberately an integration-side adapter: plugins only see immutable chat snapshots.
    internal static class BetterChatRuntime
    {
        private static int _caret;
        private static int _selectionAnchor = -1;
        private static string _lastText = string.Empty;
        private static string _hoveredMessage = string.Empty;
        private static int _hoveredTick;
        private static Keys _repeatKey = Keys.None;
        private static int _repeatStartTick;
        private static int _repeatLastTick;
        private static readonly ConditionalWeakTable<TextSnippet, ChatLineContext> ChatLines = new ConditionalWeakTable<TextSnippet, ChatLineContext>();
        private const int RepeatDelayMilliseconds = 320;
        private const int RepeatIntervalMilliseconds = 38;

        internal static string Process(PluginChatHost host, IPluginUserInteractionService userInteraction, string oldString, bool allowMultiLine)
        {
            string text = oldString ?? string.Empty;
            SynchronizeCaret(text);
            Main.inputTextEnter = false;
            Main.inputTextEscape = false;

            KeyboardState current = Main.inputText;
            KeyboardState previous = Main.oldInputText;
            KeyboardState navigation = Main.keyState;
            KeyboardState oldNavigation = Main.oldKeyState;
            bool control = navigation.IsKeyDown(Keys.LeftControl) || navigation.IsKeyDown(Keys.RightControl);
            bool shift = navigation.IsKeyDown(Keys.LeftShift) || navigation.IsKeyDown(Keys.RightShift);

            if (control && !current.IsKeyDown(Keys.LeftAlt) && !current.IsKeyDown(Keys.RightAlt))
            {
                if (Pressed(current, previous, Keys.A))
                {
                    _selectionAnchor = 0;
                    _caret = text.Length;
                }
                else if (Pressed(current, previous, Keys.V))
                {
                    userInteraction.TryReadClipboard(out string pasted);
                    text = Insert(text, NormalizeInput(pasted, allowMultiLine));
                }
                else if (Pressed(current, previous, Keys.C) || Pressed(current, previous, Keys.Insert))
                {
                    userInteraction.TryWriteClipboard(SelectedOrAll(text));
                }
                else if (Pressed(current, previous, Keys.X))
                {
                    userInteraction.TryWriteClipboard(SelectedOrAll(text));
                    bool hadSelection = HasSelection;
                    text = hadSelection ? DeleteSelection(text) : string.Empty;
                    if (!hadSelection)
                        _caret = 0;
                }
            }
            else
            {
                int count = Math.Max(0, Math.Min(Main.keyCount, Math.Min(Main.keyInt.Length, Main.keyString.Length)));
                for (int index = 0; index < count; index++)
                {
                    int key = Main.keyInt[index];
                    if (key == 13)
                        Main.inputTextEnter = true;
                    else if (key == 27)
                        Main.inputTextEscape = true;
                    else if (key >= 32 && key != 127)
                        text = Insert(text, NormalizeInput(Main.keyString[index] ?? string.Empty, allowMultiLine));
                }
            }

            text = Edit(host, text, navigation, oldNavigation, Keys.Left, "left", control, shift);
            text = Edit(host, text, navigation, oldNavigation, Keys.Right, "right", control, shift);
            if (Pressed(navigation, oldNavigation, Keys.Home)) text = Apply(host, text, "home", control, shift);
            if (Pressed(navigation, oldNavigation, Keys.End)) text = Apply(host, text, "end", control, shift);
            if (Repeated(navigation, oldNavigation, Keys.Back)) text = HasSelection ? DeleteSelection(text) : RemoveBefore(text, control);
            if (Repeated(navigation, oldNavigation, Keys.Delete)) text = HasSelection ? DeleteSelection(text) : RemoveAfter(text, control);
            if (Pressed(navigation, oldNavigation, Keys.Escape)) Main.inputTextEscape = true;

            Main.keyCount = 0;
            Main.oldInputText = current;
            Main.inputText = Keyboard.GetState();
            _caret = Clamp(_caret, 0, text.Length);
            _lastText = text;
            return text;
        }

        internal static string FormatForDraw(bool active, string text)
        {
            text = text ?? string.Empty;
            if (!active)
                return Main.instance != null && Main.instance.textBlinkerState == 1 ? text + "|" : text;

            SynchronizeCaret(text);
            string cursor = Main.instance != null && Main.instance.textBlinkerState == 1 ? "|" : " ";
            if (!HasSelection)
                return text.Insert(_caret, cursor);

            int start = Math.Min(_caret, _selectionAnchor);
            int end = Math.Max(_caret, _selectionAnchor);
            string selected = EscapeTagText(text.Substring(start, end - start));
            const string prefix = "[c/80B8FF:";
            const string suffix = "]";
            string display = text.Substring(0, start) + prefix + selected + suffix + text.Substring(end);
            int cursorIndex = _caret <= start ? start : start + prefix.Length + selected.Length + suffix.Length;
            return display.Insert(Clamp(cursorIndex, 0, display.Length), cursor);
        }

        internal static object Decorate(PluginChatHost host, object snippets, Color baseColor, string originalMessage)
        {
            if (!(snippets is IList list) || list.Count == 0)
                return snippets;

            for (int index = 0; index < list.Count; index++)
            {
                if (!(list[index] is TextSnippet snippet))
                    continue;

                ChatLines.Remove(snippet);
                ChatLines.Add(snippet, new ChatLineContext(originalMessage ?? snippet.TextOriginal ?? snippet.Text));
                snippet.CheckForHover = true;
                if (snippet.GetType() != typeof(TextSnippet))
                    continue;

                IReadOnlyList<ChatTextSpan> spans = host.Decorate(new ChatMessageSnapshot(snippet.Text));
                if (spans.Count == 1 && spans[0].LinkTarget == null && spans[0].Text == snippet.Text)
                    continue;

                list.RemoveAt(index);
                for (int spanIndex = spans.Count - 1; spanIndex >= 0; spanIndex--)
                {
                    ChatTextSpan span = spans[spanIndex];
                    TextSnippet replacement = span.LinkTarget == null
                        ? new TextSnippet(span.Text, snippet.Color)
                        : new AlacrityLinkTextSnippet(span.Text, span.LinkTarget, new Color(90, 175, 255));
                    replacement.TextOriginal = originalMessage ?? snippet.TextOriginal;
                    replacement.CheckForHover = true;
                    ChatLines.Add(replacement, new ChatLineContext(originalMessage ?? snippet.TextOriginal ?? snippet.Text));
                    list.Insert(index, replacement);
                }
                index += spans.Count - 1;
            }
            return snippets;
        }

        internal static void Hover(object value, IPluginUserInteractionService userInteraction)
        {
            if (!(value is TextSnippet snippet))
                return;

            _hoveredMessage = ChatLines.TryGetValue(snippet, out ChatLineContext line) ? line.Text : snippet.TextOriginal ?? snippet.Text ?? string.Empty;
            _hoveredTick = Environment.TickCount;
            if (Main.mouseRight && Main.mouseRightRelease)
            {
                userInteraction.TryWriteClipboard(_hoveredMessage);
                Main.mouseRightRelease = false;
            }
        }

        internal static bool Click(PluginChatHost host, object value)
        {
            if (!(value is AlacrityLinkTextSnippet link) || !TryHttpUri(link.Target, out Uri uri))
                return false;
            return host.TryActivate(uri);
        }

        internal static Color VisibleColor(object value, Color color)
        {
            if (!(value is TextSnippet snippet) || Elapsed(Environment.TickCount, _hoveredTick) > 120)
                return color;
            bool highlight = ChatLines.TryGetValue(snippet, out ChatLineContext line) && string.Equals(line.Text, _hoveredMessage, StringComparison.Ordinal);
            return highlight ? Color.Lerp(color, new Color(255, 245, 150), 0.45f) : color;
        }

        internal static void CopyContext(object source, object copy)
        {
            if (source is TextSnippet sourceSnippet && copy is TextSnippet copySnippet && ChatLines.TryGetValue(sourceSnippet, out ChatLineContext line))
            {
                ChatLines.Remove(copySnippet);
                ChatLines.Add(copySnippet, line);
            }
        }

        private static string Edit(PluginChatHost host, string text, KeyboardState current, KeyboardState old, Keys key, string action, bool control, bool shift)
        {
            return Repeated(current, old, key) ? Apply(host, text, action, control, shift) : text;
        }

        private static string Apply(PluginChatHost host, string text, string action, bool control, bool shift)
        {
            ChatInputEditResult result = host.Edit(new ChatInputSnapshot(text, _caret, _selectionAnchor), new ChatInputAction(action, control, shift));
            if (!result.Handled)
                return text;
            _caret = result.Caret;
            _selectionAnchor = result.SelectionAnchor;
            return result.Text;
        }

        private static string Insert(string text, string value)
        {
            if (string.IsNullOrEmpty(value)) return text;
            if (HasSelection) text = DeleteSelection(text);
            text = text.Insert(_caret, value);
            _caret += value.Length;
            return text;
        }

        private static string RemoveBefore(string text, bool control)
        {
            if (_caret == 0) return text;
            int start = control ? PreviousWord(text, _caret) : PreviousUnit(text, _caret);
            text = text.Remove(start, _caret - start);
            _caret = start;
            return text;
        }

        private static string RemoveAfter(string text, bool control)
        {
            if (_caret >= text.Length) return text;
            int end = control ? NextWord(text, _caret) : NextUnit(text, _caret);
            return text.Remove(_caret, end - _caret);
        }

        private static string DeleteSelection(string text)
        {
            int start = Math.Min(_caret, _selectionAnchor);
            int end = Math.Max(_caret, _selectionAnchor);
            _caret = start;
            _selectionAnchor = -1;
            return text.Remove(start, end - start);
        }

        private static int PreviousWord(string text, int index) { while (index > 0 && char.IsWhiteSpace(text, PreviousUnit(text, index))) index = PreviousUnit(text, index); while (index > 0 && !char.IsWhiteSpace(text, PreviousUnit(text, index))) index = PreviousUnit(text, index); return index; }
        private static int NextWord(string text, int index) { while (index < text.Length && !char.IsWhiteSpace(text, index)) index = NextUnit(text, index); while (index < text.Length && char.IsWhiteSpace(text, index)) index = NextUnit(text, index); return index; }
        private static int PreviousUnit(string text, int index) { return TryGetChatTagAt(text, Math.Max(0, index - 1), out int start, out _) ? start : PreviousScalar(text, index); }
        private static int NextUnit(string text, int index) { return TryGetChatTagAt(text, Math.Min(index, text.Length - 1), out _, out int end) ? end : NextScalar(text, index); }
        private static bool TryGetChatTagAt(string text, int index, out int start, out int end)
        {
            start = end = -1;
            if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length) return false;
            int opening = text.LastIndexOf('[', index);
            if (opening < 0 || opening + 3 >= text.Length) return false;
            char kind = text[opening + 1];
            char syntax = text[opening + 2];
            if ((kind != 'i' && kind != 'g') || (syntax != ':' && !(kind == 'i' && syntax == '/'))) return false;
            int closing = text.IndexOf(']', opening + 3);
            if (closing < 0 || index > closing) return false;
            start = opening; end = closing + 1; return true;
        }
        private static int PreviousScalar(string text, int index) => index > 1 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2]) ? index - 2 : Math.Max(0, index - 1);
        private static int NextScalar(string text, int index) => index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? index + 2 : Math.Min(text.Length, index + 1);
        private static bool Pressed(KeyboardState current, KeyboardState old, Keys key) => current.IsKeyDown(key) && !old.IsKeyDown(key);
        private static bool Repeated(KeyboardState current, KeyboardState old, Keys key)
        {
            if (!current.IsKeyDown(key))
            {
                if (_repeatKey == key) _repeatKey = Keys.None;
                return false;
            }

            int now = Environment.TickCount;
            if (!old.IsKeyDown(key) || _repeatKey != key)
            {
                _repeatKey = key;
                _repeatStartTick = now;
                _repeatLastTick = now;
                return true;
            }

            if (Elapsed(now, _repeatStartTick) < RepeatDelayMilliseconds || Elapsed(now, _repeatLastTick) < RepeatIntervalMilliseconds)
                return false;
            _repeatLastTick = now;
            return true;
        }
        private static bool HasSelection => _selectionAnchor >= 0 && _selectionAnchor != _caret;
        private static string SelectedOrAll(string text) => HasSelection ? text.Substring(Math.Min(_caret, _selectionAnchor), Math.Abs(_caret - _selectionAnchor)) : text;
        private static void SynchronizeCaret(string text) { if (!string.Equals(_lastText, text, StringComparison.Ordinal)) { _caret = text.Length; _selectionAnchor = -1; _lastText = text; } _caret = Clamp(_caret, 0, text.Length); }
        private static bool TryHttpUri(string value, out Uri uri) => Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        private static int Elapsed(int current, int previous) => unchecked(current - previous);
        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
        private static string EscapeTagText(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("]", "\\]");
        private static string NormalizeInput(string value, bool allowMultiLine) => allowMultiLine ? value : (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ");
        private sealed class ChatLineContext
        {
            internal ChatLineContext(string text) { Text = text ?? string.Empty; }
            internal string Text { get; }
        }
    }

    internal sealed class AlacrityLinkTextSnippet : TextSnippet
    {
        internal AlacrityLinkTextSnippet(string text, string target, Color color) : base(text, color) { Target = target; }
        internal string Target { get; private set; }
    }
}
