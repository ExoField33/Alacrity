using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Registers validated plugin commands.
public interface IPluginCommandService
{
    /// Registers a command owned by the current plugin.
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
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A command ID is required.", nameof(id)) : id;
        HelpText = string.IsNullOrWhiteSpace(helpText) ? throw new ArgumentException("Help text is required.", nameof(helpText)) : helpText;
    }

    /// Stable command identifier within the current plugin.
    public string Id { get; }

    /// User-facing help text.
    public string HelpText { get; }
}

/// Validated command invocation arguments.
public sealed class PluginCommandInvocation
{
    /// Creates an invocation snapshot.
    public PluginCommandInvocation(IReadOnlyList<string> arguments, Action<string>? reply = null)
    {
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
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

