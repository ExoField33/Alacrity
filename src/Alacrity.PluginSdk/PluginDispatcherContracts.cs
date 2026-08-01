using System;

namespace Alacrity.PluginSdk;

/// <summary>Queues plugin work onto the host's documented main-thread boundary.</summary>
public interface IPluginDispatcher
{
    /// <summary>Whether the caller is currently on the host main thread.</summary>
    bool IsMainThread { get; }

    /// <summary>Queues callback work. The registration is cancelled automatically with the owning scope.</summary>
    IPluginRegistration Post(Action callback);
}
