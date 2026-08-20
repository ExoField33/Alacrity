using System;

namespace AlacrityTerraria.Input;

/// <summary>
/// Stateful, framework-neutral editing operations for Terraria's native text fields. The caller
/// owns keyboard, clipboard, and rendering integration; this type only owns caret/selection
/// semantics and is deliberately reusable by a test host.
/// </summary>
internal sealed class NativeTextEditState
{
    private string lastText = string.Empty;
    private int caret;
    private int selectionAnchor = -1;
    private bool synchronized;
    // UITextBox instances are version-locked native UI objects.  Retaining one only for the
    // current edit presentation prevents equal strings in unrelated fields sharing a caret.
    private object presentationIdentity;

    internal int Caret => caret;

    internal int SelectionAnchor => selectionAnchor;

    internal bool HasSelection => selectionAnchor >= 0 && selectionAnchor != caret;

    internal int GetCaret(string text)
    {
        return TryGetPresentation(text, out int currentCaret, out _, out _)
            ? currentCaret
            : (text ?? string.Empty).Length;
    }

    /// <summary>
    /// Gets the current non-mutating rendering state for the exact field that accepted the latest
    /// input. A stale draw must not resynchronize the editor and move another field's caret.
    /// </summary>
    internal bool TryGetPresentation(string text, out int currentCaret, out int selectionStart, out int selectionEnd)
    {
        text = text ?? string.Empty;
        if (!synchronized || !string.Equals(lastText, text, StringComparison.Ordinal))
        {
            currentCaret = text.Length;
            selectionStart = 0;
            selectionEnd = 0;
            return false;
        }

        currentCaret = Clamp(caret, 0, text.Length);
        selectionStart = HasSelection ? SelectionStart() : currentCaret;
        selectionEnd = HasSelection ? SelectionEnd() : currentCaret;
        return true;
    }

    /// <summary>
    /// Returns presentation only for the concrete native field that first establishes the
    /// current edit display.  A different field with identical text deliberately falls back to
    /// vanilla positioning instead of borrowing another field's caret or selection.
    /// </summary>
    internal bool TryGetPresentation(string text, object identity, out int currentCaret, out int selectionStart, out int selectionEnd)
    {
        text = text ?? string.Empty;
        if (identity == null || !synchronized || !string.Equals(lastText, text, StringComparison.Ordinal))
        {
            currentCaret = text.Length;
            selectionStart = 0;
            selectionEnd = 0;
            return false;
        }

        if (presentationIdentity == null)
        {
            presentationIdentity = identity;
        }

        if (!ReferenceEquals(presentationIdentity, identity))
        {
            currentCaret = text.Length;
            selectionStart = 0;
            selectionEnd = 0;
            return false;
        }

        currentCaret = Clamp(caret, 0, text.Length);
        selectionStart = HasSelection ? SelectionStart() : currentCaret;
        selectionEnd = HasSelection ? SelectionEnd() : currentCaret;
        return true;
    }

    /// <summary>
    /// Gets presentation state for a caller that has independently established that it is drawing
    /// the focused field. Terraria's player chat can trim its text after input and before draw,
    /// so it needs this narrow variant rather than losing the caret to a harmless normalization.
    /// </summary>
    internal bool TryGetFocusedPresentation(string text, out int currentCaret, out int selectionStart, out int selectionEnd)
    {
        text = text ?? string.Empty;
        if (!synchronized)
        {
            currentCaret = text.Length;
            selectionStart = 0;
            selectionEnd = 0;
            return false;
        }

        currentCaret = Clamp(caret, 0, text.Length);
        selectionStart = HasSelection ? Clamp(SelectionStart(), 0, text.Length) : currentCaret;
        selectionEnd = HasSelection ? Clamp(SelectionEnd(), 0, text.Length) : currentCaret;
        return true;
    }

    internal void Reset()
    {
        lastText = string.Empty;
        caret = 0;
        selectionAnchor = -1;
        synchronized = false;
        presentationIdentity = null;
    }

    internal void Synchronize(string text)
    {
        text = text ?? string.Empty;
        if (!synchronized || !string.Equals(lastText, text, StringComparison.Ordinal))
        {
            caret = text.Length;
            selectionAnchor = -1;
            synchronized = true;
            presentationIdentity = null;
        }

        caret = Clamp(caret, 0, text.Length);
        if (selectionAnchor >= 0)
        {
            selectionAnchor = Clamp(selectionAnchor, 0, text.Length);
        }
    }

    internal void Complete(string text)
    {
        lastText = text ?? string.Empty;
        caret = Clamp(caret, 0, lastText.Length);
        if (selectionAnchor >= 0)
        {
            selectionAnchor = Clamp(selectionAnchor, 0, lastText.Length);
        }

        synchronized = true;
    }

    internal void Replace(string text, int requestedCaret, int requestedSelectionAnchor)
    {
        lastText = text ?? string.Empty;
        caret = Clamp(requestedCaret, 0, lastText.Length);
        selectionAnchor = requestedSelectionAnchor < 0
            ? -1
            : Clamp(requestedSelectionAnchor, 0, lastText.Length);
        synchronized = true;
        presentationIdentity = null;
    }

    internal void SelectAll(string text)
    {
        text = text ?? string.Empty;
        selectionAnchor = 0;
        caret = text.Length;
    }

    internal void MoveLeft(string text, bool byWord, bool extendSelection)
    {
        text = text ?? string.Empty;
        if (!extendSelection && HasSelection)
        {
            caret = SelectionStart();
            selectionAnchor = -1;
            return;
        }

        BeginOrClearSelection(extendSelection);
        caret = byWord ? PreviousWord(text, caret) : PreviousUnit(text, caret);
    }

    internal void MoveRight(string text, bool byWord, bool extendSelection)
    {
        text = text ?? string.Empty;
        if (!extendSelection && HasSelection)
        {
            caret = SelectionEnd();
            selectionAnchor = -1;
            return;
        }

        BeginOrClearSelection(extendSelection);
        caret = byWord ? NextWord(text, caret) : NextUnit(text, caret);
    }

    internal void MoveHome(bool extendSelection)
    {
        BeginOrClearSelection(extendSelection);
        caret = 0;
    }

    internal void MoveEnd(string text, bool extendSelection)
    {
        BeginOrClearSelection(extendSelection);
        caret = (text ?? string.Empty).Length;
    }

    internal string Insert(string text, string value)
    {
        text = text ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return text;
        }

        if (HasSelection)
        {
            text = DeleteSelection(text);
        }

        text = text.Insert(caret, value);
        caret += value.Length;
        return text;
    }

    internal string Backspace(string text, bool byWord)
    {
        text = text ?? string.Empty;
        if (HasSelection)
        {
            return DeleteSelection(text);
        }

        if (caret == 0)
        {
            return text;
        }

        int start = byWord ? PreviousWord(text, caret) : PreviousUnit(text, caret);
        text = text.Remove(start, caret - start);
        caret = start;
        return text;
    }

    internal string Delete(string text, bool byWord)
    {
        text = text ?? string.Empty;
        if (HasSelection)
        {
            return DeleteSelection(text);
        }

        if (caret >= text.Length)
        {
            return text;
        }

        int end = byWord ? NextWord(text, caret) : NextUnit(text, caret);
        return text.Remove(caret, end - caret);
    }

    internal string SelectedOrAll(string text)
    {
        text = text ?? string.Empty;
        return HasSelection
            ? text.Substring(SelectionStart(), SelectionEnd() - SelectionStart())
            : text;
    }

    internal string FormatForPlayerChat(string text, bool drawCaret)
    {
        text = text ?? string.Empty;
        if (!TryGetFocusedPresentation(text, out int currentCaret, out int selectionStart, out int selectionEnd))
        {
            return text + (drawCaret ? "|" : " ");
        }

        string cursor = drawCaret ? "|" : " ";
        if (selectionStart == selectionEnd)
        {
            return text.Insert(currentCaret, cursor);
        }

        string selected = EscapeChatTagText(text.Substring(selectionStart, selectionEnd - selectionStart));
        const string colorPrefix = "[c/80B8FF:";
        const string colorSuffix = "]";
        string display = text.Substring(0, selectionStart) + colorPrefix + selected + colorSuffix + text.Substring(selectionEnd);
        int cursorIndex = currentCaret <= selectionStart
            ? selectionStart
            : selectionStart + colorPrefix.Length + selected.Length + colorSuffix.Length;
        return display.Insert(Clamp(cursorIndex, 0, display.Length), cursor);
    }

    /// <summary>
    /// Repositions Terraria's already-appended input ticker without changing the actual field
    /// value. Old menu fields render a separate display string, unlike UITextBox and chat.
    /// </summary>
    internal string FormatNativeDisplayText(string displayText)
    {
        if (!synchronized || string.IsNullOrEmpty(displayText))
        {
            return displayText ?? string.Empty;
        }

        int lastIndex = displayText.Length - 1;
        char ticker = displayText[lastIndex];
        if (ticker != '|' && ticker != ' ')
        {
            return displayText;
        }

        string text = displayText.Substring(0, lastIndex);
        // Password menus replace the field with a same-length mask before appending the ticker.
        // The ticker is only appended by the active input path, so matching length is the safe
        // presentation identity when the stored value is intentionally hidden.
        if (!string.Equals(lastText, text, StringComparison.Ordinal) && lastText.Length != text.Length)
        {
            return displayText;
        }

        return text.Insert(Clamp(caret, 0, text.Length), ticker.ToString());
    }

    private string DeleteSelection(string text)
    {
        int start = SelectionStart();
        int end = SelectionEnd();
        caret = start;
        selectionAnchor = -1;
        return text.Remove(start, end - start);
    }

    private void BeginOrClearSelection(bool extendSelection)
    {
        if (extendSelection)
        {
            if (selectionAnchor < 0)
            {
                selectionAnchor = caret;
            }

            return;
        }

        selectionAnchor = -1;
    }

    private int SelectionStart()
    {
        return selectionAnchor < 0 ? caret : Math.Min(selectionAnchor, caret);
    }

    private int SelectionEnd()
    {
        return selectionAnchor < 0 ? caret : Math.Max(selectionAnchor, caret);
    }

    private static int PreviousWord(string text, int index)
    {
        int current = Clamp(index, 0, text.Length);
        while (current > 0 && char.IsWhiteSpace(text, PreviousUnit(text, current)))
        {
            current = PreviousUnit(text, current);
        }

        while (current > 0 && !char.IsWhiteSpace(text, PreviousUnit(text, current)))
        {
            current = PreviousUnit(text, current);
        }

        return current;
    }

    private static int NextWord(string text, int index)
    {
        int current = Clamp(index, 0, text.Length);
        while (current < text.Length && !char.IsWhiteSpace(text, current))
        {
            current = NextUnit(text, current);
        }

        while (current < text.Length && char.IsWhiteSpace(text, current))
        {
            current = NextUnit(text, current);
        }

        return current;
    }

    private static int PreviousUnit(string text, int index)
    {
        return TryGetTerrariaTagAt(text, Math.Max(0, index - 1), out int start, out _)
            ? start
            : PreviousScalar(text, index);
    }

    private static int NextUnit(string text, int index)
    {
        return TryGetTerrariaTagAt(text, Math.Min(index, text.Length - 1), out _, out int end)
            ? end
            : NextScalar(text, index);
    }

    private static bool TryGetTerrariaTagAt(string text, int index, out int start, out int end)
    {
        start = -1;
        end = -1;
        if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length)
        {
            return false;
        }

        int opening = text.LastIndexOf('[', index);
        if (opening < 0 || opening + 3 >= text.Length)
        {
            return false;
        }

        char kind = text[opening + 1];
        char syntax = text[opening + 2];
        if ((kind != 'i' && kind != 'g') || (syntax != ':' && !(kind == 'i' && syntax == '/')))
        {
            return false;
        }

        int closing = text.IndexOf(']', opening + 3);
        if (closing < 0 || index > closing)
        {
            return false;
        }

        start = opening;
        end = closing + 1;
        return true;
    }

    private static int PreviousScalar(string text, int index)
    {
        int current = Clamp(index, 0, text.Length);
        return current > 1 && char.IsLowSurrogate(text[current - 1]) && char.IsHighSurrogate(text[current - 2])
            ? current - 2
            : Math.Max(0, current - 1);
    }

    private static int NextScalar(string text, int index)
    {
        int current = Clamp(index, 0, text.Length);
        return current + 1 < text.Length && char.IsHighSurrogate(text[current]) && char.IsLowSurrogate(text[current + 1])
            ? current + 2
            : Math.Min(text.Length, current + 1);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    private static string EscapeChatTagText(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("]", "\\]");
    }
}
