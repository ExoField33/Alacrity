using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Host-mediated service publication and discovery.
public interface IPluginServiceRegistry
{
    /// Publishes a contract implementation owned by the current plugin.
    IPluginRegistration Publish<TService>(TService service) where TService : class;

    /// Gets an active service contract without referencing its provider implementation.
    bool TryGet<TService>(out TService? service) where TService : class;

    /// Gets a declared dependency service or throws a clear availability error.
    TService GetRequired<TService>() where TService : class;
}

