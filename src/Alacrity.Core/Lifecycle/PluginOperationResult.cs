using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host diagnostic outcome for one lifecycle operation.</summary>
public sealed class PluginOperationResult
{
    internal PluginOperationResult(string operation, PluginLifecycleState resultingState, Exception? callbackFailure, IEnumerable<PluginCleanupFailure>? cleanupFailures)
    {
        Operation = operation;
        ResultingState = resultingState;
        CallbackFailure = callbackFailure == null ? null : new PluginOperationFailure(operation, callbackFailure);
        CleanupFailures = (cleanupFailures ?? Enumerable.Empty<PluginCleanupFailure>()).ToArray();
    }

    /// <summary>Lifecycle operation that produced this result.</summary>
    public string Operation { get; }
    /// <summary>State after every required recovery attempt completed.</summary>
    public PluginLifecycleState ResultingState { get; }
    /// <summary>The original plugin callback failure, if one occurred.</summary>
    public PluginOperationFailure? CallbackFailure { get; }
    /// <summary>Cleanup failures recorded without replacing a callback failure.</summary>
    public IReadOnlyList<PluginCleanupFailure> CleanupFailures { get; }
    /// <summary>Whether all callback and cleanup work succeeded.</summary>
    public bool Succeeded => CallbackFailure == null && CleanupFailures.Count == 0;
}

/// <summary>Original failure from a plugin lifecycle callback.</summary>
public sealed class PluginOperationFailure
{
    internal PluginOperationFailure(string operation, Exception exception)
    {
        Operation = operation;
        Exception = exception;
    }

    /// <summary>Callback operation that failed.</summary>
    public string Operation { get; }
    /// <summary>Original exception, preserved for diagnostics and rethrowing.</summary>
    public Exception Exception { get; }
}

/// <summary>Failure observed while the host forced cleanup after a lifecycle operation.</summary>
public sealed class PluginCleanupFailure
{
    internal PluginCleanupFailure(string phase, Exception exception)
    {
        Phase = phase;
        Exception = exception;
    }

    /// <summary>Cleanup phase that failed.</summary>
    public string Phase { get; }
    /// <summary>Cleanup exception retained for host diagnostics.</summary>
    public Exception Exception { get; }
}
