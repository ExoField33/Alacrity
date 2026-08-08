using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Plugin-scoped typed settings boundary. Persistence and recovery are host-owned.
public interface IPluginSettings
{
    /// Registers a typed setting whose persistence is owned by the host.
    IPluginSetting<T> Register<T>(PluginSettingDefinition<T> definition);

    /// Gets a stored value or the supplied default.
    T Get<T>(string key, T defaultValue);

    /// Stores a validated setting value.
    void Set<T>(string key, T value);

    /// Removes a stored key.
    bool Remove(string key);

    /// Restores the plugin's registered default values.
    void ResetToDefaults();

    /// Raised after a setting changes.
    event EventHandler<PluginSettingChangedEventArgs> Changed;
}

/// Immutable declaration for one plugin-owned typed setting.
public sealed class PluginSettingDefinition<T>
{
    /// Creates a typed setting declaration with an optional normalizer.
    public PluginSettingDefinition(string key, T defaultValue, Func<T, T>? normalize = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        DefaultValue = defaultValue;
        Normalize = normalize;
    }

    /// Stable persisted key within the plugin namespace.
    public string Key { get; }
    /// Value returned when no valid persisted value exists.
    public T DefaultValue { get; }
    /// Optional host-applied normalization before values are exposed or persisted.
    public Func<T, T>? Normalize { get; }
}

/// Host-owned typed setting handle. Subscriptions are released with the owning plugin scope.
public interface IPluginSetting<T>
{
    /// Stable persisted key within the owning plugin namespace.
    string Key { get; }
    /// Declared default value.
    T DefaultValue { get; }
    /// Current normalized persisted value.
    T Value { get; set; }
    /// Restores the declared default value.
    void Reset();
    /// Subscribes to value changes with host-managed lifetime ownership.
    IPluginRegistration Subscribe(Action<T> handler);
}

/// Describes one plugin setting change.
public sealed class PluginSettingChangedEventArgs : EventArgs
{
    /// Creates a setting change notification.
    public PluginSettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// Changed key within the current plugin's settings namespace.
    public string Key { get; }

    /// Previous value, when one existed.
    public object? OldValue { get; }

    /// New value, when one exists.
    public object? NewValue { get; }
}

