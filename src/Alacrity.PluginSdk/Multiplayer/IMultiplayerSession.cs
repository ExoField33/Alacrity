using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Read-only multiplayer session state supplied by the host.
public interface IMultiplayerSession
{
    /// Whether the client has an active multiplayer connection.
    bool IsConnected { get; }

    /// Whether the session remains compatible with vanilla servers.
    bool IsVanillaCompatibleMode { get; }

    /// Whether the connected server understands Alacrity policy negotiation.
    bool IsAlacrityAwareServer { get; }

    /// Current server identity, when connected.
    ServerIdentity? Server { get; }

    /// Current host-validated server policy, when available.
    ServerPluginPolicySnapshot? ActivePolicy { get; }
}

/// Read-only server identity.
public sealed class ServerIdentity
{
    /// Creates a server identity.
    public ServerIdentity(string address, string? displayName = null)
    {
        Address = string.IsNullOrWhiteSpace(address) ? throw new ArgumentException("A server address is required.", nameof(address)) : address;
        DisplayName = displayName;
    }

    /// Host and port used for the active session.
    public string Address { get; }

    /// Server-provided display name, when available.
    public string? DisplayName { get; }
}

/// Immutable effective policy state; desired user state never overrides a denial.
public sealed class ServerPluginPolicySnapshot
{
    /// Creates a policy snapshot.
    public ServerPluginPolicySnapshot(IReadOnlyCollection<PluginId> deniedPlugins)
    {
        DeniedPlugins = deniedPlugins ?? throw new ArgumentNullException(nameof(deniedPlugins));
    }

    /// Plugins denied by the active server policy.
    public IReadOnlyCollection<PluginId> DeniedPlugins { get; }

    /// Whether the policy denies a plugin.
    public bool IsDenied(PluginId pluginId)
    {
        foreach (var deniedPlugin in DeniedPlugins)
        {
            if (deniedPlugin == pluginId)
                return true;
        }

        return false;
    }
}
