using System;

namespace AlacrityTerraria.UserInterface;

/// <summary>
/// Small UI-owned text buffer for host search fields. It keeps cursor and selection behavior out
/// of plugin code while providing the familiar word-navigation/deletion shortcuts that Terraria's
/// append-only text helper does not expose.
/// </summary>
internal sealed class PluginSearchTextBuffer
{
    private const int MaximumLength = 48;
    private string text = string.Empty;
    private int caret;
    private int selectionAnchor = -1;

    internal string Text => text;

    internal int Caret => caret;

    /// <summary>Gets the current selection without exposing mutable text-edit state.</summary>
    internal bool TryGetSelection(out int start, out int end)
    {
        if (selectionAnchor < 0 || selectionAnchor == caret)
        {
            start = caret;
            end = caret;
            return false;
        }

        start = SelectionStart();
        end = start + SelectedLength();
        return true;
    }

    internal void Clear()
    {
        text = string.Empty;
        caret = 0;
        selectionAnchor = -1;
    }

    internal bool Insert(string value)
    {
        if (!TryBuildInsertedText(value, out string result))
        {
            return false;
        }

        int start = SelectionStart();
        int selectedLength = SelectedLength();
        int available = MaximumLength - (text.Length - selectedLength);
        int insertedLength = Math.Min(value.Length, available);
        text = result;
        caret = start + insertedLength;
        selectionAnchor = -1;
        return true;
    }

    /// <summary>
    /// Produces the exact text that <see cref="Insert"/> would commit without changing cursor or
    /// selection state. Specialized search controls use this to validate an edit before accepting
    /// input that would otherwise leave their native view without a valid result.
    /// </summary>
    internal bool TryBuildInsertedText(string value, out string result)
    {
        result = text;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int selectedLength = SelectedLength();
        int available = MaximumLength - (text.Length - selectedLength);
        if (available <= 0)
        {
            return false;
        }

        string insertion = value.Length > available ? value.Substring(0, available) : value;
        int start = SelectionStart();
        result = text.Remove(start, selectedLength).Insert(start, insertion);
        return true;
    }

    internal bool Backspace(bool byWord)
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (caret == 0)
        {
            return false;
        }

        int start = byWord ? FindPreviousWordBoundary(caret) : caret - 1;
        text = text.Remove(start, caret - start);
        caret = start;
        return true;
    }

    internal bool Delete(bool byWord)
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (caret >= text.Length)
        {
            return false;
        }

        int end = byWord ? FindNextWordBoundary(caret) : caret + 1;
        text = text.Remove(caret, end - caret);
        return true;
    }

    internal void MoveLeft(bool byWord, bool extendSelection)
    {
        int target = byWord ? FindPreviousWordBoundary(caret) : Math.Max(0, caret - 1);
        MoveTo(target, extendSelection);
    }

    internal void MoveRight(bool byWord, bool extendSelection)
    {
        int target = byWord ? FindNextWordBoundary(caret) : Math.Min(text.Length, caret + 1);
        MoveTo(target, extendSelection);
    }

    internal void MoveHome(bool extendSelection)
    {
        MoveTo(0, extendSelection);
    }

    internal void MoveEnd(bool extendSelection)
    {
        MoveTo(text.Length, extendSelection);
    }

    internal void SelectAll()
    {
        selectionAnchor = 0;
        caret = text.Length;
    }

    private void MoveTo(int target, bool extendSelection)
    {
        target = Math.Max(0, Math.Min(text.Length, target));
        if (extendSelection)
        {
            if (selectionAnchor < 0)
            {
                selectionAnchor = caret;
            }
        }
        else
        {
            selectionAnchor = -1;
        }

        caret = target;
    }

    private bool DeleteSelection()
    {
        int length = SelectedLength();
        if (length == 0)
        {
            return false;
        }

        int start = SelectionStart();
        text = text.Remove(start, length);
        caret = start;
        selectionAnchor = -1;
        return true;
    }

    private int FindPreviousWordBoundary(int index)
    {
        int current = Math.Max(0, Math.Min(text.Length, index));
        while (current > 0 && !IsWordCharacter(text[current - 1]))
        {
            current--;
        }

        while (current > 0 && IsWordCharacter(text[current - 1]))
        {
            current--;
        }

        return current;
    }

    private int FindNextWordBoundary(int index)
    {
        int current = Math.Max(0, Math.Min(text.Length, index));
        while (current < text.Length && !IsWordCharacter(text[current]))
        {
            current++;
        }

        while (current < text.Length && IsWordCharacter(text[current]))
        {
            current++;
        }

        return current;
    }

    private int SelectionStart()
    {
        return selectionAnchor < 0 ? caret : Math.Min(selectionAnchor, caret);
    }

    private int SelectedLength()
    {
        return selectionAnchor < 0 ? 0 : Math.Abs(selectionAnchor - caret);
    }

    private static bool IsWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_' || value == '-';
    }
}
