using System;

namespace Alacrity.PluginSdk;

/// <summary>Stable, lowercase identifier for an Alacrity plugin.</summary>
public readonly struct PluginId : IEquatable<PluginId>
{
    /// <summary>Maximum number of characters in a package-safe plugin identifier.</summary>
    public const int MaximumLength = 96;

    /// <summary>Creates an identifier and validates its package-safe format.</summary>
    public PluginId(string value)
    {
        if (!TryValidate(value, out var error))
            throw new ArgumentException(error, nameof(value));

        Value = value;
    }

    /// <summary>Canonical identifier text.</summary>
    public string Value { get; }

    /// <summary>Whether this instance contains a valid non-default identifier.</summary>
    public bool IsValid => TryValidate(Value, out _);

    /// <summary>Parses a package-safe plugin identifier.</summary>
    public static PluginId Parse(string value) => new PluginId(value);

    /// <summary>Attempts to parse a package-safe plugin identifier.</summary>
    public static bool TryParse(string? value, out PluginId id)
    {
        if (TryValidate(value, out _))
        {
            id = new PluginId(value!);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Compares identifiers using ordinal equality.</summary>
    public bool Equals(PluginId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PluginId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Compares two plugin identifiers.</summary>
    public static bool operator ==(PluginId left, PluginId right) => left.Equals(right);

    /// <summary>Compares two plugin identifiers for inequality.</summary>
    public static bool operator !=(PluginId left, PluginId right) => !left.Equals(right);

    private static bool TryValidate(string? value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A plugin ID is required.";
            return false;
        }

        var text = value!;
        if (text.Length > MaximumLength)
        {
            error = "Plugin IDs cannot exceed " + MaximumLength + " characters.";
            return false;
        }

        if (text[0] == '.' || text[0] == '-' || text[text.Length - 1] == '.' || text[text.Length - 1] == '-')
        {
            error = "Plugin IDs cannot begin or end with a separator.";
            return false;
        }

        var previousWasSeparator = false;
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            var separator = character == '-' || character == '.';
            if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && !separator)
            {
                error = "Plugin IDs may contain lowercase letters, digits, '-' and '.'.";
                return false;
            }

            if (separator && previousWasSeparator)
            {
                error = "Plugin IDs cannot contain empty segments.";
                return false;
            }

            previousWasSeparator = separator;
        }

        error = string.Empty;
        return true;
    }
}
