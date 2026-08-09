using System;
using System.Collections.Generic;
using System.Linq;

namespace Alacrity.PluginSdk;

/// <summary>Supported framework-neutral value forms for typed command parameters.</summary>
public enum PluginCommandValueKind
{
    /// <summary>A text argument, including text captured from a quoted token.</summary>
    String,

    /// <summary>A signed 32-bit integer argument.</summary>
    Int32,

    /// <summary>A finite single-precision floating-point argument.</summary>
    Single,

    /// <summary>A boolean argument accepting documented boolean spellings.</summary>
    Boolean,

    /// <summary>A declared enumeration value parsed without case sensitivity.</summary>
    Enum,

    /// <summary>One of a fixed, case-insensitive set of declared string choices.</summary>
    Choice
}

/// <summary>Immutable help and completion metadata for one command parameter.</summary>
public sealed class PluginCommandParameterDescriptor
{
    /// <summary>Creates normalized parameter metadata.</summary>
    public PluginCommandParameterDescriptor(
        string name,
        PluginCommandValueKind valueKind,
        bool isRequired,
        string? description = null,
        string? defaultValue = null,
        double? minimum = null,
        double? maximum = null,
        IEnumerable<string>? choices = null)
    {
        if (!Enum.IsDefined(typeof(PluginCommandValueKind), valueKind))
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
        {
            throw new ArgumentException("A parameter name without whitespace is required.", nameof(name));
        }

        if ((minimum.HasValue && !IsFinite(minimum.Value)) ||
            (maximum.HasValue && !IsFinite(maximum.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Numeric command bounds must be finite.");
        }

        if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "A parameter minimum cannot exceed its maximum.");
        }

        Name = name;
        ValueKind = valueKind;
        IsRequired = isRequired;
        Description = description ?? string.Empty;
        DefaultValue = defaultValue;
        Minimum = minimum;
        Maximum = maximum;
        Choices = CopyChoices(choices);
    }

    /// <summary>Stable command-local name.</summary>
    public string Name { get; }

    /// <summary>Expected input form.</summary>
    public PluginCommandValueKind ValueKind { get; }

    /// <summary>Whether this parameter must be supplied.</summary>
    public bool IsRequired { get; }

    /// <summary>Optional help text.</summary>
    public string Description { get; }

    /// <summary>Formatted optional default value.</summary>
    public string? DefaultValue { get; }

    /// <summary>Inclusive numeric minimum when applicable.</summary>
    public double? Minimum { get; }

    /// <summary>Inclusive numeric maximum when applicable.</summary>
    public double? Maximum { get; }

    /// <summary>Known values suitable for completion or help.</summary>
    public IReadOnlyList<string> Choices { get; }

    private static IReadOnlyList<string> CopyChoices(IEnumerable<string>? choices)
    {
        if (choices == null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (string choice in choices)
        {
            if (string.IsNullOrWhiteSpace(choice))
            {
                throw new ArgumentException("Command choices cannot be empty.", nameof(choices));
            }

            if (result.Any(existing => string.Equals(existing, choice, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Command choices must be unique.", nameof(choices));
            }

            result.Add(choice);
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
