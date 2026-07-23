using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// <summary>One host-owned registration that is released with its plugin resource scope.</summary>
public interface IPluginRegistration : IDisposable
{
    /// <summary>Stable diagnostic name for the registration.</summary>
    string Name { get; }

    /// <summary>Whether the host has released the registration.</summary>
    bool IsReleased { get; }
}

/// <summary>Asynchronous lifecycle for plugins loaded from a host-verified package manifest.</summary>
public interface IAsyncAlacrityPlugin
{
    /// <summary>Initializes plugin state from the host-supplied verified context.</summary>
    Task InitializeAsync(IPluginContextV2 context, CancellationToken cancellationToken);

    /// <summary>Activates registrations and runtime work.</summary>
    Task EnableAsync(CancellationToken cancellationToken);

    /// <summary>Stops runtime work before scope cleanup.</summary>
    Task DisableAsync(CancellationToken cancellationToken);

    /// <summary>Releases plugin-owned managed state.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>Expanded plugin context for packages whose manifest was verified before DLL execution.</summary>
public interface IPluginContextV2 : IPluginContext
{
    /// <summary>Plugin-scoped settings service.</summary>
    IPluginSettings Settings { get; }

    /// <summary>Path-confined plugin data storage.</summary>
    IPluginStorage Storage { get; }

    /// <summary>Typed snapshot event subscriptions.</summary>
    IPluginEventService Events { get; }

    /// <summary>Scoped command registrations.</summary>
    IPluginCommandService Commands { get; }

    /// <summary>Scoped keybind registrations.</summary>
    IPluginKeybindService Keybinds { get; }

    /// <summary>UI contribution registrations interpreted by the host application.</summary>
    IPluginUiService Ui { get; }

    /// <summary>Read-only multiplayer session and server policy state.</summary>
    IMultiplayerSession Multiplayer { get; }
}

/// <summary>Plugin-scoped typed settings boundary. Persistence and recovery are host-owned.</summary>
public interface IPluginSettings
{
    /// <summary>Gets a stored value or the supplied default.</summary>
    T Get<T>(string key, T defaultValue);

    /// <summary>Stores a validated setting value.</summary>
    void Set<T>(string key, T value);

    /// <summary>Removes a stored key.</summary>
    bool Remove(string key);

    /// <summary>Restores the plugin's registered default values.</summary>
    void ResetToDefaults();

    /// <summary>Raised after a setting changes.</summary>
    event EventHandler<PluginSettingChangedEventArgs> Changed;
}

/// <summary>Describes one plugin setting change.</summary>
public sealed class PluginSettingChangedEventArgs : EventArgs
{
    /// <summary>Creates a setting change notification.</summary>
    public PluginSettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>Changed key within the current plugin's settings namespace.</summary>
    public string Key { get; }

    /// <summary>Previous value, when one existed.</summary>
    public object? OldValue { get; }

    /// <summary>New value, when one exists.</summary>
    public object? NewValue { get; }
}

/// <summary>Path-confined storage for one plugin's data directory.</summary>
public interface IPluginStorage
{
    /// <summary>Opens a plugin-owned relative file for reading.</summary>
    Stream OpenRead(string relativePath);

    /// <summary>Creates or replaces a plugin-owned relative file.</summary>
    Stream Create(string relativePath);

    /// <summary>Checks a plugin-owned relative path.</summary>
    bool Exists(string relativePath);

    /// <summary>Deletes a plugin-owned relative file.</summary>
    void Delete(string relativePath);

    /// <summary>Lists paths beneath a plugin-owned relative directory.</summary>
    IReadOnlyList<string> Enumerate(string relativeDirectory);
}

/// <summary>Typed snapshot event subscriptions. Handlers run on the host-documented affinity for each event.</summary>
public interface IPluginEventService
{
    /// <summary>Subscribes a handler that is automatically removed when its resource scope is released.</summary>
    IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null);
}

/// <summary>Subscription delivery options.</summary>
public sealed class PluginEventOptions
{
    /// <summary>Whether host dispatch should stop this subscription after its first delivery.</summary>
    public bool Once { get; set; }
}

/// <summary>Registers validated plugin commands.</summary>
public interface IPluginCommandService
{
    /// <summary>Registers a command owned by the current plugin.</summary>
    IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler);
}

/// <summary>Immutable command declaration.</summary>
public sealed class PluginCommandDescriptor
{
    /// <summary>Creates a command declaration.</summary>
    public PluginCommandDescriptor(string id, string helpText)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A command ID is required.", nameof(id)) : id;
        HelpText = string.IsNullOrWhiteSpace(helpText) ? throw new ArgumentException("Help text is required.", nameof(helpText)) : helpText;
    }

    /// <summary>Stable command identifier within the current plugin.</summary>
    public string Id { get; }

    /// <summary>User-facing help text.</summary>
    public string HelpText { get; }
}

/// <summary>Validated command invocation arguments.</summary>
public sealed class PluginCommandInvocation
{
    /// <summary>Creates an invocation snapshot.</summary>
    public PluginCommandInvocation(IReadOnlyList<string> arguments) => Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));

    /// <summary>Immutable argument list.</summary>
    public IReadOnlyList<string> Arguments { get; }
}

/// <summary>Registers user-rebindable plugin keybinds.</summary>
public interface IPluginKeybindService
{
    /// <summary>Registers a keybind owned by the current plugin.</summary>
    IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler);
}

/// <summary>Immutable keybind declaration.</summary>
public sealed class PluginKeybindDescriptor
{
    /// <summary>Creates a keybind declaration.</summary>
    public PluginKeybindDescriptor(string id, string defaultBinding, string displayName)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A keybind ID is required.", nameof(id)) : id;
        DefaultBinding = string.IsNullOrWhiteSpace(defaultBinding) ? throw new ArgumentException("A default binding is required.", nameof(defaultBinding)) : defaultBinding;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName;
    }

    /// <summary>Stable keybind identifier within the current plugin.</summary>
    public string Id { get; }

    /// <summary>Host-parseable default binding.</summary>
    public string DefaultBinding { get; }

    /// <summary>User-facing label.</summary>
    public string DisplayName { get; }
}

/// <summary>Registers UI contributions; the host controls actual layout and rendering.</summary>
public interface IPluginUiService
{
    /// <summary>Registers a settings-page contribution.</summary>
    IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution);

    /// <summary>Registers an overlay contribution.</summary>
    IPluginRegistration RegisterOverlay(PluginUiContribution contribution);
}

/// <summary>Host-rendered UI contribution metadata.</summary>
public sealed class PluginUiContribution
{
    /// <summary>Creates a contribution declaration.</summary>
    public PluginUiContribution(string id, string displayName)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A UI contribution ID is required.", nameof(id)) : id;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName;
    }

    /// <summary>Stable contribution identifier within the current plugin.</summary>
    public string Id { get; }

    /// <summary>User-facing display name.</summary>
    public string DisplayName { get; }
}

/// <summary>Host-mediated service publication and discovery.</summary>
public interface IPluginServiceRegistry
{
    /// <summary>Publishes a contract implementation owned by the current plugin.</summary>
    IPluginRegistration Publish<TService>(TService service) where TService : class;

    /// <summary>Gets an active service contract without referencing its provider implementation.</summary>
    bool TryGet<TService>(out TService? service) where TService : class;

    /// <summary>Gets a declared dependency service or throws a clear availability error.</summary>
    TService GetRequired<TService>() where TService : class;
}

/// <summary>Read-only multiplayer session state supplied by the host.</summary>
public interface IMultiplayerSession
{
    /// <summary>Whether the client has an active multiplayer connection.</summary>
    bool IsConnected { get; }

    /// <summary>Whether the session remains compatible with vanilla servers.</summary>
    bool IsVanillaCompatibleMode { get; }

    /// <summary>Whether the connected server understands Alacrity policy negotiation.</summary>
    bool IsAlacrityAwareServer { get; }

    /// <summary>Current server identity, when connected.</summary>
    ServerIdentity? Server { get; }

    /// <summary>Current host-validated server policy, when available.</summary>
    ServerPluginPolicySnapshot? ActivePolicy { get; }
}

/// <summary>Read-only server identity.</summary>
public sealed class ServerIdentity
{
    /// <summary>Creates a server identity.</summary>
    public ServerIdentity(string address, string? displayName = null)
    {
        Address = string.IsNullOrWhiteSpace(address) ? throw new ArgumentException("A server address is required.", nameof(address)) : address;
        DisplayName = displayName;
    }

    /// <summary>Host and port used for the active session.</summary>
    public string Address { get; }

    /// <summary>Server-provided display name, when available.</summary>
    public string? DisplayName { get; }
}

/// <summary>Immutable effective policy state; desired user state never overrides a denial.</summary>
public sealed class ServerPluginPolicySnapshot
{
    /// <summary>Creates a policy snapshot.</summary>
    public ServerPluginPolicySnapshot(IReadOnlyCollection<PluginId> deniedPlugins)
    {
        DeniedPlugins = deniedPlugins ?? throw new ArgumentNullException(nameof(deniedPlugins));
    }

    /// <summary>Plugins denied by the active server policy.</summary>
    public IReadOnlyCollection<PluginId> DeniedPlugins { get; }

    /// <summary>Whether the policy denies a plugin.</summary>
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
