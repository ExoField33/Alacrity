using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// One host-owned registration that is released with its plugin resource scope.
public interface IPluginRegistration : IDisposable
{
    /// Stable diagnostic name for the registration.
    string Name { get; }

    /// Whether the host has released the registration.
    bool IsReleased { get; }
}

