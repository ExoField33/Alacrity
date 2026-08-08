using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Asynchronous lifecycle for plugins loaded from a host-verified package manifest.
public interface IAsyncAlacrityPlugin
{
    /// Initializes plugin state from the host-supplied verified context.
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);

    /// Activates registrations and runtime work.
    Task EnableAsync(CancellationToken cancellationToken);

    /// Stops runtime work before scope cleanup.
    Task DisableAsync(CancellationToken cancellationToken);

    /// Releases plugin-owned managed state.
    Task ShutdownAsync(CancellationToken cancellationToken);
}

