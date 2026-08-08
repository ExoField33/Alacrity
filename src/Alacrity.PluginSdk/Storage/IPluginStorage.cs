using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Path-confined storage for one plugin's data directory.
public interface IPluginStorage
{
    /// Opens a plugin-owned relative file for reading.
    Stream OpenRead(string relativePath);

    /// Creates or replaces a plugin-owned relative file.
    Stream Create(string relativePath);

    /// Checks a plugin-owned relative path.
    bool Exists(string relativePath);

    /// Deletes a plugin-owned relative file.
    void Delete(string relativePath);

    /// Lists paths beneath a plugin-owned relative directory.
    IReadOnlyList<string> Enumerate(string relativeDirectory);
}

