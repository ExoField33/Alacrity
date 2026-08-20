using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.UI.Chat;

namespace AlacrityTerraria
{
    // This is deliberately an integration-side adapter: plugins only see immutable chat snapshots.
    /// <summary>Terraria-specific adapter for generic chat extension registrations.</summary>
    internal static class TerrariaChatRuntime
    {
        private static int _caret;
        private static int _selectionAnchor = -1;
        private static string _lastText = string.Empty;
        private static string _hoveredMessage = string.Empty;
        private static int _hoveredTick;
        private static long _nextMessageHandle;
        private static int _readyOutgoingSubmission;
        private static int _storedMessageDecorationDepth;
        private static readonly Dictionary<Keys, RepeatState> RepeatStates = new Dictionary<Keys, RepeatState>();
        private static readonly ConditionalWeakTable<TextSnippet, ChatLineContext> ChatLines = new ConditionalWeakTable<TextSnippet, ChatLineContext>();
        private static readonly ConditionalWeakTable<ChatMessageContainer, ChatContainerContext> ChatContainers = new ConditionalWeakTable<ChatMessageContainer, ChatContainerContext>();
        private static readonly object StoredMessageRefreshGate = new object();
        private static readonly Dictionary<long, WeakReference> ContainerByMessageHandle = new Dictionary<long, WeakReference>();
        private static readonly Queue<long> ContainerHandleOrder = new Queue<long>();
        private static readonly HashSet<long> PendingPresentationRefreshes = new HashSet<long>();
        private static readonly Queue<long> PresentationRefreshQueue = new Queue<long>();
        private static ChatMessageContainer _storedMessageContainer;
        private static ChatContainerContext _storedMessageContainerContext;
        private static FieldInfo storedMessagePreparedField;
        private static int storedMessagePreparedFieldResolved;
        private const int RepeatDelayMilliseconds = 320;
        private const int RepeatIntervalMilliseconds = 38;
        private const int MaximumRetainedMessageContainers = 768;
        internal const int MaximumChatScrollLinesPerAction = 16;

        internal static string Process(PluginChatHost host, IPluginUserInteractionService userInteraction, string oldString, bool allowMultiLine)
        {
            string text = oldString ?? string.Empty;
            if (!FocusHelper.AllowUIInputs)
            {
                return text;
            }

            bool submitReadyOutgoing = Interlocked.Exchange(ref _readyOutgoingSubmission, 0) != 0;
            SynchronizeCaret(text);
            Main.inputTextEnter = false;
            Main.inputTextEscape = false;

            KeyboardState current = Main.inputText;
            KeyboardState previous = Main.oldInputText;
            // Terraria queues text against inputText, then obtains a fresh snapshot for held
            // edit keys.  Use that same boundary so a held arrow/backspace is not dependent on
            // whichever earlier update path last advanced Main.keyState.
            KeyboardState navigation = Keyboard.GetState();
            KeyboardState oldNavigation = Main.inputText;
            bool control = navigation.IsKeyDown(Keys.LeftControl) || navigation.IsKeyDown(Keys.RightControl);
            bool shift = navigation.IsKeyDown(Keys.LeftShift) || navigation.IsKeyDown(Keys.RightShift);

            if (Chat.TerrariaChatActionStrip.TryProcessSearchInput(current, previous))
            {
                // A host action-menu search is distinct from the chat text field. It owns this
                // raw input frame and leaves the pending player message untouched.
                Main.inputTextEnter = submitReadyOutgoing;
                Main.inputTextEscape = false;
                if (submitReadyOutgoing)
                {
                    Main.chatRelease = true;
                }
                Main.oldInputText = current;
                Main.inputText = Keyboard.GetState();
                _lastText = text;
                return text;
            }

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
            text = Edit(host, text, navigation, oldNavigation, Keys.Up, "up", control, shift);
            text = Edit(host, text, navigation, oldNavigation, Keys.Down, "down", control, shift);
            if (Pressed(navigation, oldNavigation, Keys.Home)) text = Apply(host, text, "home", control, shift);
            if (Pressed(navigation, oldNavigation, Keys.End)) text = Apply(host, text, "end", control, shift);
            if (Repeated(navigation, oldNavigation, Keys.Back)) text = HasSelection ? DeleteSelection(text) : RemoveBefore(text, control);
            if (Repeated(navigation, oldNavigation, Keys.Delete)) text = HasSelection ? DeleteSelection(text) : RemoveAfter(text, control);
            if (Pressed(navigation, oldNavigation, Keys.Escape))
            {
                if (Chat.TerrariaChatActionStrip.TryHandleEscape())
                {
                    // The raw key loop above may already have set this flag. A host-owned
                    // popover consumes Escape before Terraria's native close-chat path sees it.
                    Main.inputTextEscape = false;
                }
                else
                {
                    Main.inputTextEscape = true;
                }
            }

            int scrollLines = PlayerInput.ScrollWheelDelta / 120;
            if (scrollLines != 0 && Apply(host, text, "scroll", control, shift, scrollLines, out string scrolledText))
            {
                text = scrolledText;
                // Chat owns this wheel tick while focused. Clearing the shared delta keeps the
                // later player-update hotbar path from selecting an item as well.
                PlayerInput.ScrollWheelDelta = 0;
                PlayerInput.ScrollWheelDeltaForUI = 0;
            }

            Main.keyCount = 0;
            Main.oldInputText = current;
            Main.inputText = navigation;
            if (submitReadyOutgoing)
            {
                // A completed host transform was accepted at the input boundary. Keep its
                // synthetic submit signal through this custom editor so native chat submits it
                // this update without waiting for the player to modify the text again.
                Main.inputTextEnter = true;
                Main.chatRelease = true;
            }

            _caret = Clamp(_caret, 0, text.Length);
            _lastText = text;
            return text;
        }

        /// <summary>Requests a single native player-chat submit after an owned asynchronous
        /// outgoing transformation completes at the update boundary.</summary>
        internal static void RequestReadyOutgoingSubmission()
        {
            Interlocked.Exchange(ref _readyOutgoingSubmission, 1);
        }

        /// <summary>Returns whether a completed transform is waiting for Terraria's native
        /// player-chat submit gate. The gate consumes the normal Terraria fields; this signal
        /// only restores them if a later input helper cleared the synthetic submit frame.</summary>
        internal static bool HasReadyOutgoingSubmission()
        {
            return Volatile.Read(ref _readyOutgoingSubmission) != 0;
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

        internal static void BeginStoredMessageDecoration(object messageContainer)
        {
            Interlocked.Increment(ref _storedMessageDecorationDepth);
            if (messageContainer is ChatMessageContainer container)
            {
                ChatContainerContext context = ChatContainers.GetValue(container, _ => new ChatContainerContext());
                context.BeginRefresh();
                _storedMessageContainer = container;
                _storedMessageContainerContext = context;
            }
        }

        internal static void EndStoredMessageDecoration()
        {
            int remaining = Interlocked.Decrement(ref _storedMessageDecorationDepth);
            if (remaining < 0)
            {
                Interlocked.Exchange(ref _storedMessageDecorationDepth, 0);
            }

            if (remaining <= 0)
            {
                _storedMessageContainer = null;
                _storedMessageContainerContext = null;
            }
        }

        /// <summary>
        /// Prepares a retained chat-monitor message before Terraria applies native word wrapping.
        /// A decorator therefore sees one complete message rather than each display fragment.
        /// Only a presentation with one uniform owner action is converted back into native chat
        /// markup; more complex decorators retain the existing per-snippet fallback below.
        /// </summary>
        internal static string PrepareStoredMessageText(PluginChatHost host, object messageContainer, string originalMessage)
        {
            string text = originalMessage ?? string.Empty;
            if (host == null || !(messageContainer is ChatMessageContainer container))
            {
                return text;
            }

            ChatContainerContext context = ChatContainers.GetValue(container, _ => new ChatContainerContext());
            ChatMessageHandle handle = context.GetHandle();
            RegisterMessageContainer(container, handle);

            IReadOnlyList<ChatTextSpan> spans = host.Decorate(new ChatMessageSnapshot(text, handle));
            if (!PreparedStoredChatPresentation.TryCreate(text, spans, out PreparedStoredChatPresentation presentation))
            {
                context.ClearPreparedPresentation();
                return text;
            }

            context.SetPreparedPresentation(presentation);
            return presentation.NativeText;
        }

        /// <summary>
        /// Called from the Core host's completion notification. It only records the native
        /// container for a main-thread refresh; background translation work never accesses
        /// mutable Terraria UI objects directly.
        /// </summary>
        internal static void QueuePresentationRefresh(ChatMessageHandle handle)
        {
            if (!handle.IsValid)
            {
                return;
            }

            lock (StoredMessageRefreshGate)
            {
                if (!ContainerByMessageHandle.TryGetValue(handle.Value, out WeakReference reference) ||
                    !(reference.Target is ChatMessageContainer))
                {
                    ContainerByMessageHandle.Remove(handle.Value);
                    return;
                }

                if (PendingPresentationRefreshes.Add(handle.Value))
                {
                    PresentationRefreshQueue.Enqueue(handle.Value);
                }
            }
        }

        /// <summary>Runs at the native chat-monitor update boundary before it refreshes lines.</summary>
        internal static void RefreshQueuedMessagePresentations()
        {
            while (true)
            {
                ChatMessageContainer container = null;
                lock (StoredMessageRefreshGate)
                {
                    if (PresentationRefreshQueue.Count == 0)
                    {
                        return;
                    }

                    long handle = PresentationRefreshQueue.Dequeue();
                    PendingPresentationRefreshes.Remove(handle);
                    if (ContainerByMessageHandle.TryGetValue(handle, out WeakReference reference))
                    {
                        container = reference.Target as ChatMessageContainer;
                        if (container == null)
                        {
                            ContainerByMessageHandle.Remove(handle);
                        }
                    }
                }

                // This member is private in vanilla Terraria, unlike the tModLoader reference.
                // Resolve it once and clear it only from RemadeChatMonitor.Update, where native
                // code will immediately own the normal rewrap and preserve OriginalText.
                if (container != null)
                {
                    MarkStoredMessageForRefresh(container);
                }
            }
        }

        internal static object DecorateStoredMessage(PluginChatHost host, object snippets, Color baseColor, string originalMessage)
        {
            if (Volatile.Read(ref _storedMessageDecorationDepth) == 0)
            {
                return snippets;
            }

            if (!(snippets is IList list) || list.Count == 0)
                return snippets;

            ChatContainerContext context = _storedMessageContainerContext;
            if (context != null && context.TryGetPreparedPresentation(out PreparedStoredChatPresentation prepared))
            {
                DecoratePreparedStoredMessage(list, context.GetHandle(), prepared);
                return snippets;
            }

            for (int index = 0; index < list.Count; index++)
            {
                if (!(list[index] is TextSnippet snippet))
                    continue;

                ChatLines.Remove(snippet);
                ChatMessageHandle handle = GetStoredMessageHandle();
                RegisterMessageContainer(handle);
                ChatLines.Add(snippet, new ChatLineContext(originalMessage ?? snippet.TextOriginal ?? snippet.Text, default, handle, null, -1));
                snippet.CheckForHover = true;
                if (snippet.GetType() != typeof(TextSnippet))
                    continue;

                IReadOnlyList<ChatTextSpan> spans = host.Decorate(new ChatMessageSnapshot(snippet.Text, handle));
                if (spans.Count == 1 && spans[0].LinkTarget == null && spans[0].ActionId == null && !spans[0].Color.HasValue && spans[0].Text == snippet.Text)
                    continue;

                list.RemoveAt(index);
                var replacements = new TextSnippet[spans.Count];
                var binding = new ChatPresentationBinding(handle, replacements);
                for (int spanIndex = spans.Count - 1; spanIndex >= 0; spanIndex--)
                {
                    ChatTextSpan span = spans[spanIndex];
                    Color spanColor = span.Color.HasValue
                        ? new Color(span.Color.Value.Red, span.Color.Value.Green, span.Color.Value.Blue)
                        : snippet.Color;
                    TextSnippet replacement = span.LinkTarget == null && span.ActionId == null
                        ? new TextSnippet(span.Text, spanColor)
                        : new AlacrityInteractiveTextSnippet(span.Text, span.LinkTarget, span.ActionId, span.ActionTarget, span.Owner, spanColor);
                    replacement.TextOriginal = originalMessage ?? snippet.TextOriginal;
                    replacement.CheckForHover = true;
                    replacements[spanIndex] = replacement;
                    ChatLines.Add(replacement, new ChatLineContext(originalMessage ?? snippet.TextOriginal ?? snippet.Text, span.Owner, handle, binding, spanIndex));
                    list.Insert(index, replacement);
                }
                index += spans.Count - 1;
            }
            return snippets;
        }

        private static void DecoratePreparedStoredMessage(IList list, ChatMessageHandle handle, PreparedStoredChatPresentation presentation)
        {
            for (int index = 0; index < list.Count; index++)
            {
                if (!(list[index] is TextSnippet snippet))
                {
                    continue;
                }

                ChatLines.Remove(snippet);
                TextSnippet decorated = snippet;
                ChatPresentationBinding binding = null;
                if (presentation.HasInteraction && snippet.GetType() == typeof(TextSnippet))
                {
                    decorated = new AlacrityInteractiveTextSnippet(
                        snippet.Text,
                        presentation.LinkTarget,
                        presentation.ActionId,
                        presentation.ActionTarget,
                        presentation.Owner,
                        snippet.Color);
                    decorated.TextOriginal = presentation.OriginalText;
                    binding = new ChatPresentationBinding(handle, new[] { decorated });
                    list[index] = decorated;
                }

                decorated.CheckForHover = true;
                ChatLines.Add(decorated, new ChatLineContext(presentation.OriginalText, presentation.Owner, handle, binding, 0));
            }
        }

        private static ChatMessageHandle GetStoredMessageHandle()
        {
            ChatContainerContext context = _storedMessageContainerContext;
            return context == null
                ? new ChatMessageHandle(Interlocked.Increment(ref _nextMessageHandle))
                : context.GetHandle();
        }

        private static void RegisterMessageContainer(ChatMessageHandle handle)
        {
            ChatMessageContainer container = _storedMessageContainer;
            RegisterMessageContainer(container, handle);
        }

        private static void RegisterMessageContainer(ChatMessageContainer container, ChatMessageHandle handle)
        {
            if (!handle.IsValid || container == null)
            {
                return;
            }

            lock (StoredMessageRefreshGate)
            {
                if (!ContainerByMessageHandle.ContainsKey(handle.Value))
                {
                    ContainerHandleOrder.Enqueue(handle.Value);
                }
                ContainerByMessageHandle[handle.Value] = new WeakReference(container);
                while (ContainerHandleOrder.Count > MaximumRetainedMessageContainers)
                {
                    long expiredHandle = ContainerHandleOrder.Dequeue();
                    ContainerByMessageHandle.Remove(expiredHandle);
                    PendingPresentationRefreshes.Remove(expiredHandle);
                }
            }
        }

        private static void MarkStoredMessageForRefresh(ChatMessageContainer container)
        {
            if (Interlocked.CompareExchange(ref storedMessagePreparedFieldResolved, 1, 0) == 0)
            {
                storedMessagePreparedField = typeof(ChatMessageContainer).GetField("_prepared", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            FieldInfo field = storedMessagePreparedField;
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(container, false);
            }
        }

        internal static void Hover(object value, PluginChatHost host)
        {
            if (!(value is TextSnippet snippet))
                return;

            ApplyPresentation(snippet, host);
            _hoveredMessage = ChatLines.TryGetValue(snippet, out ChatLineContext line) ? line.Text : snippet.TextOriginal ?? snippet.Text ?? string.Empty;
            _hoveredTick = Environment.TickCount;
            if (Main.mouseRight && Main.mouseRightRelease && line != null && line.Owner.IsValid && host.TryGetInteraction(line.Owner, out IPluginUserInteractionService userInteraction) && userInteraction != null)
            {
                userInteraction.TryWriteClipboard(_hoveredMessage);
                Main.mouseRightRelease = false;
            }
        }

        internal static bool Click(PluginChatHost host, object value)
        {
            if (!(value is TextSnippet snippet))
                return false;

            ApplyPresentation(snippet, host);
            if (!TryResolveInteractiveContext(snippet, out AlacrityInteractiveTextSnippet interactive, out ChatLineContext line))
            {
                return false;
            }

            // A link is the specific thing the player clicked. Do not let a broader message
            // action (such as translation) replace that native/owner-provided interaction.
            if (TryHttpUri(interactive.Target, out Uri uri))
            {
                return host.TryActivate(uri);
            }

            return !string.IsNullOrEmpty(interactive.ActionId) && interactive.Owner.IsValid &&
                host.TryActivateMessageAction(interactive.Owner, interactive.ActionId, line.Handle, interactive.ActionTarget, Main.keyState.PressingShift());
        }

        internal static Color VisibleColor(object value, Color color, PluginChatHost host)
        {
            if (!(value is TextSnippet snippet))
                return color;
            ApplyPresentation(snippet, host);
            if (Elapsed(Environment.TickCount, _hoveredTick) > 120)
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

        private static void ApplyPresentation(TextSnippet snippet, PluginChatHost host)
        {
            if (snippet == null || !ChatLines.TryGetValue(snippet, out ChatLineContext line) || line.Binding == null || host == null)
                return;

            if (!host.TryGetMessagePresentation(line.Handle, out ChatMessagePresentation presentation, out int version) || presentation == null || version == line.Binding.Version || presentation.Spans.Count != line.Binding.Snippets.Length)
                return;

            for (int index = 0; index < line.Binding.Snippets.Length; index++)
            {
                TextSnippet target = line.Binding.Snippets[index];
                ChatTextSpan span = presentation.Spans[index];
                target.Text = span.Text;
                target.TextOriginal = span.Text;
                if (span.Color.HasValue)
                    target.Color = new Color(span.Color.Value.Red, span.Color.Value.Green, span.Color.Value.Blue);
                if (target is AlacrityInteractiveTextSnippet interactive)
                    interactive.Update(span.LinkTarget, span.ActionId, span.ActionTarget, span.Owner);
            }

            line.Binding.Version = version;
        }

        // Terraria's word wrapping uses TextSnippet.CopyMorph, which returns a base TextSnippet.
        // CopyContext preserves the owning line binding, so resolve the original interactive span
        // from that binding instead of requiring the wrapped display fragment to retain its subtype.
        private static bool TryResolveInteractiveContext(TextSnippet snippet, out AlacrityInteractiveTextSnippet interactive, out ChatLineContext line)
        {
            if (!ChatLines.TryGetValue(snippet, out line))
            {
                interactive = null;
                return false;
            }

            if (snippet is AlacrityInteractiveTextSnippet direct)
            {
                interactive = direct;
                return true;
            }

            if (line.Binding == null || line.BindingIndex < 0 || line.BindingIndex >= line.Binding.Snippets.Length ||
                !(line.Binding.Snippets[line.BindingIndex] is AlacrityInteractiveTextSnippet bound))
            {
                interactive = null;
                return false;
            }

            interactive = bound;
            return true;
        }

        private static string Edit(PluginChatHost host, string text, KeyboardState current, KeyboardState old, Keys key, string action, bool control, bool shift)
        {
            return Repeated(current, old, key) ? Apply(host, text, action, control, shift) : text;
        }

        private static string Apply(PluginChatHost host, string text, string action, bool control, bool shift)
        {
            Apply(host, text, action, control, shift, 0, out string resultText);
            return resultText;
        }

        private static bool Apply(PluginChatHost host, string text, string action, bool control, bool shift, int scrollLines, out string resultText)
        {
            ChatInputEditResult result = host.Edit(new ChatInputSnapshot(text, _caret, _selectionAnchor), new ChatInputAction(action, control, shift, null, scrollLines));
            if (!result.Handled)
            {
                resultText = text;
                return false;
            }

            _caret = result.Caret;
            _selectionAnchor = result.SelectionAnchor;
            int boundedScrollLines = Math.Max(-MaximumChatScrollLinesPerAction, Math.Min(MaximumChatScrollLinesPerAction, result.ChatScrollLines));
            if (boundedScrollLines != 0)
                Main.chatMonitor.Offset(boundedScrollLines);
            resultText = result.Text;
            return true;
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
            RepeatStates.TryGetValue(key, out RepeatState state);
            if (!current.IsKeyDown(key))
            {
                state.Held = false;
                RepeatStates[key] = state;
                return false;
            }

            int now = Environment.TickCount;
            if (!old.IsKeyDown(key) || !state.Held)
            {
                state.Held = true;
                state.StartTick = now;
                state.LastTick = now;
                RepeatStates[key] = state;
                return true;
            }

            if (Elapsed(now, state.StartTick) < RepeatDelayMilliseconds || Elapsed(now, state.LastTick) < RepeatIntervalMilliseconds)
                return false;
            state.LastTick = now;
            RepeatStates[key] = state;
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
            internal ChatLineContext(string text, PluginId owner, ChatMessageHandle handle, ChatPresentationBinding binding, int bindingIndex) { Text = text ?? string.Empty; Owner = owner; Handle = handle; Binding = binding; BindingIndex = bindingIndex; }
            internal string Text { get; }
            internal PluginId Owner { get; }
            internal ChatMessageHandle Handle { get; }
            internal ChatPresentationBinding Binding { get; }
            internal int BindingIndex { get; }
        }
        private sealed class ChatPresentationBinding
        {
            internal ChatPresentationBinding(ChatMessageHandle handle, TextSnippet[] snippets) { Handle = handle; Snippets = snippets; }
            internal ChatMessageHandle Handle { get; }
            internal TextSnippet[] Snippets { get; }
            internal int Version { get; set; }
        }
        private sealed class ChatContainerContext
        {
            private ChatMessageHandle handle;
            private PreparedStoredChatPresentation preparedPresentation;

            internal void BeginRefresh()
            {
                preparedPresentation = null;
            }

            internal ChatMessageHandle GetHandle()
            {
                if (handle.IsValid)
                {
                    return handle;
                }

                handle = new ChatMessageHandle(Interlocked.Increment(ref _nextMessageHandle));
                return handle;
            }

            internal void SetPreparedPresentation(PreparedStoredChatPresentation value)
            {
                preparedPresentation = value;
            }

            internal void ClearPreparedPresentation()
            {
                preparedPresentation = null;
            }

            internal bool TryGetPreparedPresentation(out PreparedStoredChatPresentation value)
            {
                value = preparedPresentation;
                return value != null;
            }
        }

        private sealed class PreparedStoredChatPresentation
        {
            private PreparedStoredChatPresentation(
                string originalText,
                string nativeText,
                string linkTarget,
                string actionId,
                string actionTarget,
                PluginId owner)
            {
                OriginalText = originalText;
                NativeText = nativeText;
                LinkTarget = linkTarget;
                ActionId = actionId;
                ActionTarget = actionTarget;
                Owner = owner;
            }

            internal string OriginalText { get; }
            internal string NativeText { get; }
            internal string LinkTarget { get; }
            internal string ActionId { get; }
            internal string ActionTarget { get; }
            internal PluginId Owner { get; }
            internal bool HasInteraction => !string.IsNullOrEmpty(LinkTarget) || (!string.IsNullOrEmpty(ActionId) && Owner.IsValid);

            internal static bool TryCreate(string originalText, IReadOnlyList<ChatTextSpan> spans, out PreparedStoredChatPresentation presentation)
            {
                presentation = null;
                if (spans == null || spans.Count == 0)
                {
                    return false;
                }

                ChatTextSpan first = spans[0];
                if (first == null || (string.IsNullOrEmpty(first.ActionId) && string.IsNullOrEmpty(first.LinkTarget)))
                {
                    return false;
                }

                for (int index = 1; index < spans.Count; index++)
                {
                    ChatTextSpan span = spans[index];
                    if (span == null ||
                        !string.Equals(span.LinkTarget, first.LinkTarget, StringComparison.Ordinal) ||
                        !string.Equals(span.ActionId, first.ActionId, StringComparison.Ordinal) ||
                        !string.Equals(span.ActionTarget, first.ActionTarget, StringComparison.Ordinal) ||
                        span.Owner != first.Owner)
                    {
                        return false;
                    }
                }

                var builder = new StringBuilder();
                for (int index = 0; index < spans.Count; index++)
                {
                    AppendNativeText(builder, spans[index]);
                }

                presentation = new PreparedStoredChatPresentation(
                    originalText ?? string.Empty,
                    builder.ToString(),
                    first.LinkTarget,
                    first.ActionId,
                    first.ActionTarget,
                    first.Owner);
                return true;
            }

            private static void AppendNativeText(StringBuilder builder, ChatTextSpan span)
            {
                string text = span.Text ?? string.Empty;
                // Core uses an empty second span to keep an asynchronous presentation's shape
                // stable. Terraria treats an empty color tag as literal text, so omit it from
                // the native source while retaining that internal span for later updates.
                if (text.Length == 0)
                {
                    return;
                }

                if (!span.Color.HasValue)
                {
                    builder.Append(text);
                    return;
                }

                PluginColor color = span.Color.Value;
                builder.Append("[c/");
                AppendHex(builder, color.Red);
                AppendHex(builder, color.Green);
                AppendHex(builder, color.Blue);
                builder.Append(':');
                builder.Append(EscapeTagText(text));
                builder.Append(']');
            }

            private static void AppendHex(StringBuilder builder, byte value)
            {
                const string Digits = "0123456789ABCDEF";
                builder.Append(Digits[value >> 4]);
                builder.Append(Digits[value & 15]);
            }
        }
        private struct RepeatState
        {
            internal bool Held;
            internal int StartTick;
            internal int LastTick;
        }
    }

    internal sealed class AlacrityInteractiveTextSnippet : TextSnippet
    {
        internal AlacrityInteractiveTextSnippet(string text, string target, string actionId, string actionTarget, PluginId owner, Color color) : base(text, color) { Target = target; ActionId = actionId; ActionTarget = actionTarget ?? string.Empty; Owner = owner; }
        internal string Target { get; private set; }
        internal string ActionId { get; private set; }
        internal string ActionTarget { get; private set; }
        internal PluginId Owner { get; private set; }
        internal void Update(string target, string actionId, string actionTarget, PluginId owner) { Target = target; ActionId = actionId; ActionTarget = actionTarget ?? string.Empty; Owner = owner; }
    }
}
