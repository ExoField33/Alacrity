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

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\\' && quote != '\0')
            {
                if (index + 1 < text.Length)
                {
                    char next = text[index + 1];
                    bool escapesActiveQuote = next == quote;
                    if (next == '\\' || escapesActiveQuote)
                    {
                        EnsureCurrent(ref current).Append(next);
                        index++;
                        continue;
                    }
                }

                // A backslash is only an escape before a backslash or the active quote. This
                // preserves ordinary Windows paths such as C:\\Games\\Terraria.
                EnsureCurrent(ref current).Append(character);
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

        if (quote != '\0')
        {
            tokens = Array.Empty<string>();
            error = "Unclosed quoted command argument.";
            return false;
        }

        CompleteToken(result, ref current);
        tokens = Array.AsReadOnly(result.ToArray());
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
