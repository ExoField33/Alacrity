using System;
using System.Collections.Generic;
using System.Text;

namespace Alacrity.PluginSdk;

/// <summary>Framework-neutral command tokenization shared by command entry points and tests.</summary>
public static class PluginCommandTokenizer
{
    /// <summary>Splits whitespace-separated arguments while preserving single- or double-quoted text and escaped quotes.</summary>
    public static bool TryTokenize(string text, out IReadOnlyList<string> tokens, out string? error)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var result = new List<string>();
        StringBuilder? current = null;
        char quote = '\0';
        bool escaped = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (escaped)
            {
                EnsureCurrent(ref current).Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\0')
            {
                escaped = true;
                continue;
            }

            if (character == '\'' || character == '"')
            {
                if (quote == '\0')
                {
                    quote = character;
                    EnsureCurrent(ref current);
                    continue;
                }

                if (quote == character)
                {
                    quote = '\0';
                    continue;
                }
            }

            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                CompleteToken(result, ref current);
                continue;
            }

            EnsureCurrent(ref current).Append(character);
        }

        if (escaped)
        {
            EnsureCurrent(ref current).Append('\\');
        }

        if (quote != '\0')
        {
            tokens = Array.Empty<string>();
            error = "Unclosed quoted command argument.";
            return false;
        }

        CompleteToken(result, ref current);
        tokens = result.ToArray();
        error = null;
        return true;
    }

    private static StringBuilder EnsureCurrent(ref StringBuilder? current)
    {
        if (current == null)
        {
            current = new StringBuilder();
        }

        return current;
    }

    private static void CompleteToken(List<string> tokens, ref StringBuilder? current)
    {
        if (current == null)
        {
            return;
        }

        tokens.Add(current.ToString());
        current = null;
    }
}
