using System;
using System.Collections.Generic;
using System.Globalization;

namespace Alacrity.PluginSdk;

/// <summary>Creates a typed command declaration that ultimately registers through <see cref="IPluginCommandService"/>.</summary>
public static class PluginCommandServiceExtensions
{
    /// <summary>Starts a fluent, activation-scoped typed command declaration.</summary>
    public static PluginTypedCommandBuilder Define(this IPluginCommandService commands, string id, string helpText)
    {
        if (commands == null)
        {
            throw new ArgumentNullException(nameof(commands));
        }

        return new PluginTypedCommandBuilder(commands, id, helpText);
    }
}

/// <summary>
/// Initialization-time builder for a typed command. It compiles into one ordinary descriptor and
/// one ordinary command-host registration; it does not introduce another dispatch path.
/// </summary>
public sealed class PluginTypedCommandBuilder
{
    private readonly IPluginCommandService commands;
    private readonly string id;
    private readonly string helpText;
    private readonly List<string> aliases = new List<string>();
    private readonly List<IPluginTypedCommandBinding> bindings = new List<IPluginTypedCommandBinding>();
    private bool hasOptionalParameter;
    private bool registered;

    internal PluginTypedCommandBuilder(IPluginCommandService commands, string id, string helpText)
    {
        this.commands = commands;
        this.id = id;
        this.helpText = helpText;
    }

    /// <summary>Adds an alternative command name.</summary>
    public PluginTypedCommandBuilder Alias(string alias)
    {
        EnsureMutable();
        aliases.Add(alias);
        return this;
    }

    /// <summary>Declares one required token string.</summary>
    public PluginTypedCommandParameter<string> RequiredString(string name, string? description = null, Func<string, string?>? validator = null)
    {
        return AddRequired(name, PluginCommandValueKind.String, description, TryParseString, null, null, null, validator);
    }

    /// <summary>Declares one optional token string.</summary>
    public PluginTypedCommandParameter<string> OptionalString(string name, string defaultValue, string? description = null, Func<string, string?>? validator = null)
    {
        return AddOptional(name, PluginCommandValueKind.String, defaultValue, description, TryParseString, null, null, null, validator);
    }

    /// <summary>Declares one required invariant-culture 32-bit integer.</summary>
    public PluginTypedCommandParameter<int> RequiredInt32(string name, int? minimum = null, int? maximum = null, string? description = null, Func<int, string?>? validator = null)
    {
        ValidateRange(minimum, maximum, nameof(minimum));
        return AddRequired(name, PluginCommandValueKind.Int32, description, TryParseInt32, minimum, maximum, null, validator);
    }

    /// <summary>Declares one optional invariant-culture 32-bit integer.</summary>
    public PluginTypedCommandParameter<int> OptionalInt32(string name, int defaultValue, int? minimum = null, int? maximum = null, string? description = null, Func<int, string?>? validator = null)
    {
        ValidateRange(minimum, maximum, nameof(minimum));
        return AddOptional(name, PluginCommandValueKind.Int32, defaultValue, description, TryParseInt32, minimum, maximum, null, validator);
    }

    /// <summary>Declares one required invariant-culture floating-point value.</summary>
    public PluginTypedCommandParameter<float> RequiredSingle(string name, float? minimum = null, float? maximum = null, string? description = null, Func<float, string?>? validator = null)
    {
        ValidateRange(minimum, maximum, nameof(minimum));
        return AddRequired(name, PluginCommandValueKind.Single, description, TryParseSingle, minimum, maximum, null, validator);
    }

    /// <summary>Declares one optional invariant-culture floating-point value.</summary>
    public PluginTypedCommandParameter<float> OptionalSingle(string name, float defaultValue, float? minimum = null, float? maximum = null, string? description = null, Func<float, string?>? validator = null)
    {
        ValidateRange(minimum, maximum, nameof(minimum));
        return AddOptional(name, PluginCommandValueKind.Single, defaultValue, description, TryParseSingle, minimum, maximum, null, validator);
    }

    /// <summary>Declares one required boolean accepting true/false, yes/no, on/off, and 1/0.</summary>
    public PluginTypedCommandParameter<bool> RequiredBoolean(string name, string? description = null, Func<bool, string?>? validator = null)
    {
        return AddRequired(name, PluginCommandValueKind.Boolean, description, TryParseBoolean, null, null, BooleanChoices, validator);
    }

    /// <summary>Declares one optional boolean accepting true/false, yes/no, on/off, and 1/0.</summary>
    public PluginTypedCommandParameter<bool> OptionalBoolean(string name, bool defaultValue, string? description = null, Func<bool, string?>? validator = null)
    {
        return AddOptional(name, PluginCommandValueKind.Boolean, defaultValue, description, TryParseBoolean, null, null, BooleanChoices, validator);
    }

    /// <summary>Declares one required named enum value.</summary>
    public PluginTypedCommandParameter<TEnum> RequiredEnum<TEnum>(string name, string? description = null, Func<TEnum, string?>? validator = null)
        where TEnum : struct, Enum
    {
        var parser = CreateEnumParser<TEnum>(out string[] enumNames);
        return AddRequired(name, PluginCommandValueKind.Enum, description, parser, null, null, enumNames, validator);
    }

    /// <summary>Declares one optional named enum value.</summary>
    public PluginTypedCommandParameter<TEnum> OptionalEnum<TEnum>(string name, TEnum defaultValue, string? description = null, Func<TEnum, string?>? validator = null)
        where TEnum : struct, Enum
    {
        var parser = CreateEnumParser<TEnum>(out string[] enumNames);
        return AddOptional(name, PluginCommandValueKind.Enum, defaultValue, description, parser, null, null, enumNames, validator);
    }

    /// <summary>Declares one required named choice. The configured spelling is returned to the handler.</summary>
    public PluginTypedCommandParameter<string> RequiredChoice(string name, IEnumerable<string> choices, string? description = null, Func<string, string?>? validator = null)
    {
        var parser = CreateChoiceParser(choices, out string[] normalizedChoices);
        return AddRequired(name, PluginCommandValueKind.Choice, description, parser, null, null, normalizedChoices, validator);
    }

    /// <summary>Declares one optional named choice. The default must be one of the configured choices.</summary>
    public PluginTypedCommandParameter<string> OptionalChoice(string name, string defaultValue, IEnumerable<string> choices, string? description = null, Func<string, string?>? validator = null)
    {
        var parser = CreateChoiceParser(choices, out string[] normalizedChoices);
        if (!parser(defaultValue, out _, out string? error))
        {
            throw new ArgumentException("The optional choice default is invalid: " + error, nameof(defaultValue));
        }

        return AddOptional(name, PluginCommandValueKind.Choice, defaultValue, description, parser, null, null, normalizedChoices, validator);
    }

    /// <summary>Validates the declaration and registers it through the authoritative command host.</summary>
    public IPluginRegistration Register(Action<PluginTypedCommandArguments> handler)
    {
        EnsureMutable();
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var metadata = new PluginCommandParameterDescriptor[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            metadata[index] = bindings[index].Metadata;
        }

        var descriptor = new PluginCommandDescriptor(id, helpText, aliases, metadata);
        IPluginRegistration registration = commands.Register(descriptor, invocation => Invoke(invocation, handler));
        registered = true;
        return registration;
    }

    private void Invoke(PluginCommandInvocation invocation, Action<PluginTypedCommandArguments> handler)
    {
        if (!TryBind(invocation, out PluginTypedCommandArguments? arguments, out string? error))
        {
            invocation.Reply(error!);
            return;
        }

        handler(arguments!);
    }

    private bool TryBind(PluginCommandInvocation invocation, out PluginTypedCommandArguments? arguments, out string? error)
    {
        int suppliedCount = invocation.Arguments.Count;
        if (suppliedCount > bindings.Count)
        {
            arguments = null;
            error = "Too many arguments. Usage: /" + id + " " + FormatUsage();
            return false;
        }

        var values = new object[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            IPluginTypedCommandBinding binding = bindings[index];
            if (index >= suppliedCount)
            {
                if (binding.Metadata.IsRequired)
                {
                    arguments = null;
                    error = "Missing required argument '" + binding.Metadata.Name + "'. Usage: /" + id + " " + FormatUsage();
                    return false;
                }

                values[index] = binding.DefaultValue;
                continue;
            }

            if (!binding.TryParse(invocation.Arguments[index], out object? value, out string? parseError))
            {
                arguments = null;
                error = "Invalid value for '" + binding.Metadata.Name + "': " + parseError;
                return false;
            }

            values[index] = value!;
        }

        arguments = new PluginTypedCommandArguments(this, invocation, values);
        error = null;
        return true;
    }

    private string FormatUsage()
    {
        if (bindings.Count == 0)
        {
            return string.Empty;
        }

        var parts = new string[bindings.Count];
        for (int index = 0; index < bindings.Count; index++)
        {
            PluginCommandParameterDescriptor metadata = bindings[index].Metadata;
            parts[index] = metadata.IsRequired ? "<" + metadata.Name + ">" : "[" + metadata.Name + "]";
        }

        return string.Join(" ", parts);
    }

    private PluginTypedCommandParameter<T> AddRequired<T>(
        string name,
        PluginCommandValueKind kind,
        string? description,
        TryParseValue<T> parser,
        double? minimum,
        double? maximum,
        IEnumerable<string>? choices,
        Func<T, string?>? validator)
    {
        EnsureMutable();
        if (hasOptionalParameter)
        {
            throw new InvalidOperationException("A required typed command parameter cannot follow an optional parameter.");
        }

        var metadata = new PluginCommandParameterDescriptor(name, kind, true, description, null, minimum, maximum, choices);
        return AddBinding(metadata, parser, default!, validator, minimum, maximum);
    }

    private PluginTypedCommandParameter<T> AddOptional<T>(
        string name,
        PluginCommandValueKind kind,
        T defaultValue,
        string? description,
        TryParseValue<T> parser,
        double? minimum,
        double? maximum,
        IEnumerable<string>? choices,
        Func<T, string?>? validator)
    {
        EnsureMutable();
        hasOptionalParameter = true;
        var metadata = new PluginCommandParameterDescriptor(name, kind, false, description, FormatDefault(defaultValue), minimum, maximum, choices);
        var binding = new PluginTypedCommandBinding<T>(metadata, parser, defaultValue, validator, minimum, maximum);
        ValidateDefault(binding);
        return AddBinding(binding);
    }

    private PluginTypedCommandParameter<T> AddBinding<T>(
        PluginCommandParameterDescriptor metadata,
        TryParseValue<T> parser,
        T defaultValue,
        Func<T, string?>? validator,
        double? minimum,
        double? maximum)
    {
        return AddBinding(new PluginTypedCommandBinding<T>(metadata, parser, defaultValue, validator, minimum, maximum));
    }

    private PluginTypedCommandParameter<T> AddBinding<T>(PluginTypedCommandBinding<T> binding)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            if (string.Equals(bindings[index].Metadata.Name, binding.Metadata.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A typed command parameter with this name already exists: " + binding.Metadata.Name);
            }
        }

        int parameterIndex = bindings.Count;
        bindings.Add(binding);
        return new PluginTypedCommandParameter<T>(this, parameterIndex, binding.Metadata);
    }

    private static void ValidateDefault<T>(PluginTypedCommandBinding<T> binding)
    {
        if (!binding.TryValidate(binding.Default, out string? error))
        {
            throw new ArgumentException("The optional default for '" + binding.Metadata.Name + "' is invalid: " + error);
        }
    }

    private static void ValidateRange<T>(T? minimum, T? maximum, string parameterName)
        where T : struct, IComparable<T>
    {
        if (minimum.HasValue && maximum.HasValue && minimum.Value.CompareTo(maximum.Value) > 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A parameter minimum cannot exceed its maximum.");
        }
    }

    private static string FormatDefault<T>(T value)
    {
        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool TryParseString(string text, out string value, out string? error)
    {
        value = text ?? string.Empty;
        error = null;
        return true;
    }

    private static bool TryParseInt32(string text, out int value, out string? error)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = null;
            return true;
        }

        error = "expected an integer";
        return false;
    }

    private static bool TryParseSingle(string text, out float value, out string? error)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value))
        {
            error = null;
            return true;
        }

        error = "expected a finite number";
        return false;
    }

    private static bool TryParseBoolean(string text, out bool value, out string? error)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "true":
            case "yes":
            case "on":
            case "1":
                value = true;
                error = null;
                return true;
            case "false":
            case "no":
            case "off":
            case "0":
                value = false;
                error = null;
                return true;
            default:
                value = false;
                error = "expected true/false, yes/no, on/off, or 1/0";
                return false;
        }
    }

    private static TryParseValue<TEnum> CreateEnumParser<TEnum>(out string[] enumNames)
        where TEnum : struct, Enum
    {
        // Enum metadata is discovered once during initialization, never from the command hot path.
        enumNames = Enum.GetNames(typeof(TEnum));
        var values = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < enumNames.Length; index++)
        {
            string name = enumNames[index];
            values.Add(name, (TEnum)Enum.Parse(typeof(TEnum), name, false));
        }

        string expectedValues = "expected one of: " + string.Join(", ", enumNames);
        return (string text, out TEnum value, out string? error) =>
        {
            if (text != null && values.TryGetValue(text, out TEnum parsed))
            {
                value = parsed;
                error = null;
                return true;
            }

            value = default;
            error = expectedValues;
            return false;
        };
    }

    private static TryParseValue<string> CreateChoiceParser(IEnumerable<string> choices, out string[] normalizedChoices)
    {
        if (choices == null)
        {
            throw new ArgumentNullException(nameof(choices));
        }

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string choice in choices)
        {
            if (string.IsNullOrWhiteSpace(choice) || lookup.ContainsKey(choice))
            {
                throw new ArgumentException("Command choices must be non-empty and unique.", nameof(choices));
            }

            lookup.Add(choice, choice);
        }

        if (lookup.Count == 0)
        {
            throw new ArgumentException("At least one command choice is required.", nameof(choices));
        }

        var choicesForMetadata = new string[lookup.Count];
        lookup.Values.CopyTo(choicesForMetadata, 0);
        normalizedChoices = choicesForMetadata;
        return (string text, out string value, out string? error) =>
        {
            if (text != null && lookup.TryGetValue(text, out string parsed))
            {
                value = parsed;
                error = null;
                return true;
            }

            value = string.Empty;
            error = "expected one of: " + string.Join(", ", choicesForMetadata);
            return false;
        };
    }

    private void EnsureMutable()
    {
        if (registered)
        {
            throw new InvalidOperationException("A typed command builder can only be registered once.");
        }
    }

    private static readonly string[] BooleanChoices = { "true", "false", "yes", "no", "on", "off", "1", "0" };

    private delegate bool TryParseValue<T>(string text, out T value, out string? error);

    private interface IPluginTypedCommandBinding
    {
        PluginCommandParameterDescriptor Metadata { get; }
        object DefaultValue { get; }
        bool TryParse(string text, out object? value, out string? error);
    }

    private sealed class PluginTypedCommandBinding<T> : IPluginTypedCommandBinding
    {
        private readonly TryParseValue<T> parser;
        private readonly Func<T, string?>? validator;
        private readonly double? minimum;
        private readonly double? maximum;

        internal PluginTypedCommandBinding(
            PluginCommandParameterDescriptor metadata,
            TryParseValue<T> parser,
            T defaultValue,
            Func<T, string?>? validator,
            double? minimum,
            double? maximum)
        {
            Metadata = metadata;
            this.parser = parser;
            Default = defaultValue;
            this.validator = validator;
            this.minimum = minimum;
            this.maximum = maximum;
        }

        public PluginCommandParameterDescriptor Metadata { get; }
        public T Default { get; }
        public object DefaultValue => Default!;

        public bool TryParse(string text, out object? value, out string? error)
        {
            if (!parser(text, out T parsed, out error) || !TryValidate(parsed, out error))
            {
                value = null;
                return false;
            }

            value = parsed;
            return true;
        }

        internal bool TryValidate(T value, out string? error)
        {
            if (!IsWithinRange(value))
            {
                error = "must be between " + FormatRange(minimum) + " and " + FormatRange(maximum);
                return false;
            }

            if (validator != null)
            {
                error = validator(value);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        private bool IsWithinRange(T value)
        {
            if (!minimum.HasValue && !maximum.HasValue)
            {
                return true;
            }

            if (value is int integer)
            {
                return (!minimum.HasValue || integer >= minimum.Value) && (!maximum.HasValue || integer <= maximum.Value);
            }

            if (value is float single)
            {
                return (!minimum.HasValue || single >= minimum.Value) && (!maximum.HasValue || single <= maximum.Value);
            }

            return true;
        }

        private static string FormatRange(double? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "unbounded";
        }
    }
}

/// <summary>A strongly typed handle returned by a command-builder parameter declaration.</summary>
public sealed class PluginTypedCommandParameter<T>
{
    internal PluginTypedCommandParameter(PluginTypedCommandBuilder owner, int index, PluginCommandParameterDescriptor metadata)
    {
        Owner = owner;
        Index = index;
        Metadata = metadata;
    }

    internal PluginTypedCommandBuilder Owner { get; }
    internal int Index { get; }

    /// <summary>Normalized parameter metadata.</summary>
    public PluginCommandParameterDescriptor Metadata { get; }
}

/// <summary>Typed values for a successfully parsed command invocation.</summary>
public sealed class PluginTypedCommandArguments
{
    private readonly PluginTypedCommandBuilder owner;
    private readonly PluginCommandInvocation invocation;
    private readonly object[] values;

    internal PluginTypedCommandArguments(PluginTypedCommandBuilder owner, PluginCommandInvocation invocation, object[] values)
    {
        this.owner = owner;
        this.invocation = invocation;
        this.values = values;
    }

    /// <summary>Returns the value associated with a parameter declared by this command.</summary>
    public T Get<T>(PluginTypedCommandParameter<T> parameter)
    {
        if (parameter == null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }

        if (!ReferenceEquals(owner, parameter.Owner))
        {
            throw new ArgumentException("The parameter belongs to a different typed command declaration.", nameof(parameter));
        }

        return (T)values[parameter.Index];
    }

    /// <summary>Sends safe host-owned local feedback for the current command.</summary>
    public void Reply(string message)
    {
        invocation.Reply(message);
    }
}
