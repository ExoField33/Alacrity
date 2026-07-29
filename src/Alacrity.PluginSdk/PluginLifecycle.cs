using System;

namespace Alacrity.PluginSdk;

/// <summary>States used by the host while loading and removing a plugin.</summary>
public enum PluginLifecycleState
{
    /// <summary>Package metadata has been discovered.</summary>
    Discovered,
    /// <summary>Manifest and policy checks are running.</summary>
    Validating,
    /// <summary>Validated but inactive.</summary>
    Disabled,
    /// <summary>Enable callbacks are running.</summary>
    Enabling,
    /// <summary>Plugin callbacks and owned resources are active.</summary>
    Enabled,
    /// <summary>Disable callbacks are running.</summary>
    Disabling,
    /// <summary>A lifecycle callback failed and activation is stopped.</summary>
    Faulted,
    /// <summary>Removal cleanup is running.</summary>
    Uninstalling,
    /// <summary>Plugin has completed removal.</summary>
    Uninstalled
}

/// <summary>Broad ownership category for cleanup and diagnostics.</summary>
public enum PluginResourceKind
{
    // Values are intentionally stable for already-compiled third-party plugins.
    // The host does not persist them, but changing them would alter the public SDK ABI.
    /// <summary>Reversible assembly or integration patch.</summary>
    Patch = 0,
    /// <summary>Managed hook or detour registration.</summary>
    Hook = 1,
    /// <summary>Registered user-interface contribution.</summary>
    UserInterface = 2,
    /// <summary>Open configuration or settings resource.</summary>
    Configuration = 3,
    /// <summary>Loaded or retained game asset.</summary>
    Asset = 4,
    /// <summary>Timer that must be stopped.</summary>
    Timer = 5,
    /// <summary>Worker thread or task lifetime.</summary>
    Thread = 6,
    /// <summary>Native handle or COM lifetime.</summary>
    NativeHandle = 7,
    /// <summary>Other disposable plugin-owned resource.</summary>
    Other = 8,
    /// <summary>Typed event subscription.</summary>
    EventSubscription = 9,
    /// <summary>Registered chat or console command.</summary>
    Command = 10,
    /// <summary>Registered input keybind.</summary>
    Keybind = 11,
    /// <summary>Published cross-plugin service.</summary>
    Service = 12,
    /// <summary>Background task or worker lifetime.</summary>
    BackgroundTask = 13,
    /// <summary>File watcher lifetime.</summary>
    FileWatcher = 14,
    /// <summary>Observed network handler registration.</summary>
    NetworkHandler = 15,
    /// <summary>Rendering callback registration.</summary>
    RenderingHandler = 16,
}

/// <summary>State of a host-owned plugin resource scope.</summary>
public enum PluginResourceScopeState
{
    /// <summary>The scope accepts registrations.</summary>
    Open,
    /// <summary>The scope is releasing resources and rejects registrations.</summary>
    Releasing,
    /// <summary>The last release completed; the scope can be reopened for a new enable cycle.</summary>
    Released,
    /// <summary>The scope is permanently closed.</summary>
    Disposed
}

/// <summary>Minimal host-provided logging boundary.</summary>
public interface IPluginLogger
{
    /// <summary>Writes diagnostic detail.</summary>
    void Debug(string message);
    /// <summary>Writes informational state.</summary>
    void Info(string message);
    /// <summary>Writes a recoverable warning.</summary>
    void Warn(string message);
    /// <summary>Writes an error and optional exception details.</summary>
    void Error(string message, Exception? exception = null);
}

/// <summary>Owns all resources created by one plugin instance.</summary>
public interface IPluginResourceScope : IDisposable
{
    /// <summary>Indicates whether the scope has released its resources.</summary>
    bool IsDisposed { get; }

    /// <summary>Current ownership state.</summary>
    PluginResourceScopeState State { get; }

    /// <summary>Releases current resources while keeping the scope reusable.</summary>
    void ReleaseAll();

    /// <summary>Registers a resource for deterministic reverse-order cleanup.</summary>
    IPluginResourceHandle Own(string name, PluginResourceKind kind, IDisposable resource);

    /// <summary>Creates a child scope whose lifetime is owned by this scope.</summary>
    IPluginResourceScope CreateChildScope(string name);
}

/// <summary>Handle for one resource registered in a plugin scope.</summary>
public interface IPluginResourceHandle : IDisposable
{
    /// <summary>Diagnostic resource name.</summary>
    string Name { get; }
    /// <summary>Resource category.</summary>
    PluginResourceKind Kind { get; }
    /// <summary>Whether the underlying resource has been released.</summary>
    bool IsReleased { get; }
}

/// <summary>Host services made available during plugin initialization.</summary>
public interface IPluginContext
{
    /// <summary>Manifest for the plugin being initialized.</summary>
    PluginManifest Manifest { get; }
    /// <summary>Plugin-scoped resource owner.</summary>
    IPluginResourceScope Resources { get; }
    /// <summary>Host logger.</summary>
    IPluginLogger Logger { get; }
    /// <summary>Dependency-aware cross-plugin services scoped to this plugin.</summary>
    IPluginServiceRegistry Services { get; }
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
    /// <summary>Bounded host-owned overlay registrations with no raw rendering access.</summary>
    IPluginOverlayService Overlays { get; }
    /// <summary>Host-provided Terraria services with no raw Terraria objects exposed.</summary>
    ITerrariaServices Terraria { get; }
    /// <summary>Read-only multiplayer session and server policy state.</summary>
    IMultiplayerSession Multiplayer { get; }
}

/// <summary>Lifecycle contract implemented by an Alacrity plugin.</summary>
public interface IAlacrityPlugin
{
    /// <summary>Creates feature state and registers owned resources.</summary>
    void Initialize(IPluginContext context);
    /// <summary>Activates the initialized plugin.</summary>
    void Enable();
    /// <summary>Stops feature activity before resources are released.</summary>
    void Disable();
    /// <summary>Releases plugin-owned managed state.</summary>
    void Shutdown();
}
