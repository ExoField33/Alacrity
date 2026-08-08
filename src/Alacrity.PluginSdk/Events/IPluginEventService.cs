using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Typed snapshot event subscriptions. Handlers run on the host-documented affinity for each event.
public interface IPluginEventService
{
    /// Subscribes a handler that is automatically removed when its resource scope is released.
    IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null);
}

/// Subscription delivery options.
public sealed class PluginEventOptions
{
    /// Whether host dispatch should stop this subscription after its first delivery.
    public bool Once { get; set; }
}

