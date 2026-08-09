using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Registers validated plugin commands. Handlers run on the host command-dispatch boundary, which
/// Terraria currently invokes from its input/update thread. Handlers must stay short and must not
/// block on I/O. Registrations are activation-scoped; once teardown begins a queued command is
/// consumed locally but its plugin callback is not started.
public interface IPluginCommandService
{
    /// Registers a command owned by the current plugin. The handler is invoked on the host's
    /// command-dispatch thread and is removed automatically when the activation ends.
    IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler);
}

/// Explicit result of host command dispatch. A failed registered command is still consumed locally.
public enum PluginCommandDispatchResult
{
    /// No plugin owns the requested command.
    NotFound,
    /// A plugin command handled the invocation successfully.
    Handled,
    /// A plugin command was found and consumed but its callback failed.
    HandledWithFailure
}

/// Immutable command declaration.
public sealed class PluginCommandDescriptor
{
    /// Creates a command declaration.
    public PluginCommandDescriptor(string id, string helpText)
        : this(id, helpText, null, null)
    {
    }

    /// Creates a command declaration with optional aliases and normalized parameter metadata.
    public PluginCommandDescriptor(
        string id,
        string helpText,
        IEnumerable<string>? aliases,
        IEnumerable<PluginCommandParameterDescriptor>? parameters)
    {
        Id = ValidateName(id, nameof(id));
        HelpText = string.IsNullOrWhiteSpace(helpText) ? throw new ArgumentException("Help text is required.", nameof(helpText)) : helpText;
        Aliases = CopyAliases(aliases, Id);
        Parameters = CopyParameters(parameters);
    }

    /// Stable command identifier within the current plugin.
    public string Id { get; }

    /// User-facing help text.
    public string HelpText { get; }

    /// Alternative command names routed by the authoritative command host.
    public IReadOnlyList<string> Aliases { get; }

    /// Normalized parameter metadata for help and future completion surfaces.
    public IReadOnlyList<PluginCommandParameterDescriptor> Parameters { get; }

    private static string ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A command ID is required.", parameterName);
        }

        if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '/' }) >= 0)
        {
            throw new ArgumentException("Command IDs and aliases cannot contain whitespace or '/'.", parameterName);
        }

        return value;
    }

    private static IReadOnlyList<string> CopyAliases(IEnumerable<string>? aliases, string id)
    {
        if (aliases == null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (string alias in aliases)
        {
            string validated = ValidateName(alias, nameof(aliases));
            if (string.Equals(validated, id, StringComparison.OrdinalIgnoreCase) ||
                result.Any(existing => string.Equals(existing, validated, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Command aliases must be unique and must not repeat the command ID.", nameof(aliases));
            }

            result.Add(validated);
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private static IReadOnlyList<PluginCommandParameterDescriptor> CopyParameters(IEnumerable<PluginCommandParameterDescriptor>? parameters)
    {
        if (parameters == null)
        {
            return Array.Empty<PluginCommandParameterDescriptor>();
        }

        var result = new List<PluginCommandParameterDescriptor>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool encounteredOptional = false;
        foreach (PluginCommandParameterDescriptor parameter in parameters)
        {
            if (parameter == null)
            {
                throw new ArgumentException("Command parameter metadata cannot contain null entries.", nameof(parameters));
            }

            if (!names.Add(parameter.Name))
            {
                throw new ArgumentException("Command parameter names must be unique.", nameof(parameters));
            }

            if (parameter.IsRequired && encounteredOptional)
            {
                throw new ArgumentException("Required command parameters cannot follow optional parameters.", nameof(parameters));
            }

            encounteredOptional |= !parameter.IsRequired;
            result.Add(parameter);
        }

        return Array.AsReadOnly(result.ToArray());
    }
}

/// Validated command invocation arguments.
public sealed class PluginCommandInvocation
{
    /// Creates an invocation snapshot.
    public PluginCommandInvocation(IReadOnlyList<string> arguments, Action<string>? reply = null)
    {
        if (arguments == null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        var copy = new string[arguments.Count];
        for (int index = 0; index < arguments.Count; index++)
        {
            copy[index] = arguments[index] ?? throw new ArgumentException("Command arguments cannot contain null values.", nameof(arguments));
        }

        Arguments = Array.AsReadOnly(copy);
        this.reply = reply;
    }

    private readonly Action<string>? reply;

    /// Immutable argument list.
    public IReadOnlyList<string> Arguments { get; }

    /// Shows host-owned local feedback for this user-issued command when that UI is available.
    public void Reply(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A reply message is required.", nameof(message));
        reply?.Invoke(message);
    }
}
