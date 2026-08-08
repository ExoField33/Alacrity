using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// <summary>
/// Schedules activation-owned work. Update callbacks execute through the host's documented main
/// thread boundary; background callbacks must not access live game state directly.
/// </summary>
public interface IPluginScheduler
{
    /// <summary>Queues a callback for the next host update.</summary>
    IPluginRegistration NextUpdate(string name, Action callback);

    /// <summary>Queues a callback after the requested number of host updates.</summary>
    IPluginRegistration AfterUpdates(string name, uint updateCount, Action callback);

    /// <summary>Queues a callback repeatedly at a host-update interval.</summary>
    IPluginRegistration EveryUpdates(string name, uint updateInterval, Action callback);

    /// <summary>Queues a callback after elapsed monotonic host time.</summary>
    IPluginRegistration After(string name, TimeSpan delay, Action callback);

    /// <summary>Queues a callback repeatedly at an elapsed monotonic host-time interval.</summary>
    IPluginRegistration Every(string name, TimeSpan interval, Action callback);

    /// <summary>
    /// Starts explicitly named background work. The callback must marshal any Terraria-facing
    /// result through <see cref="IPluginDispatcher"/> and observes cancellation on activation end.
    /// </summary>
    IPluginRegistration RunBackground(string name, Func<CancellationToken, Task> callback);
}
