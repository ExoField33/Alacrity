using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned mutable data root for one plugin; package files are never stored here.</summary>
public sealed class PluginDataStore : IPluginStorage
{
    private readonly string root;

    public PluginDataStore(string alacrityRoot, PluginId pluginId)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot)) throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));
        if (!pluginId.IsValid) throw new ArgumentException("A valid plugin ID is required.", nameof(pluginId));
        root = Path.Combine(Path.GetFullPath(alacrityRoot), "data", "plugins", pluginId.Value, "user-data");
        Directory.CreateDirectory(root);
        EnsureNotReparsePoint(root);
    }

    /// <summary>Absolute plugin-confined mutable data path.</summary>
    public string RootPath => root;
    public Stream OpenRead(string relativePath) => new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);
    public Stream Create(string relativePath)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }
    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));
    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path)) File.Delete(path);
    }
    public IReadOnlyList<string> Enumerate(string relativeDirectory)
    {
        var directory = ResolveDirectory(relativeDirectory);
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        var results = new List<string>();
        foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            results.Add(path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return results;
    }
    /// <summary>Writes a complete data file through a temporary file before replacing the destination.</summary>
    public void WriteAtomically(string relativePath, byte[] contents)
    {
        if (contents == null) throw new ArgumentNullException(nameof(contents));
        var destination = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, contents);
            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) throw new UnauthorizedAccessException("Plugin storage paths must be relative.");
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        EnsureInside(path);
        EnsureExistingSegmentsAreSafe(path);
        return path;
    }
    private string ResolveDirectory(string relativeDirectory) => string.IsNullOrWhiteSpace(relativeDirectory) ? root : Resolve(relativeDirectory);
    private void EnsureInside(string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Plugin storage cannot escape its data root.");
    }
    private void EnsureExistingSegmentsAreSafe(string path)
    {
        var current = root;
        foreach (var part in path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current)) EnsureNotReparsePoint(current);
        }
    }
    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Reparse points are not allowed in plugin data.");
    }
}

/// <summary>Schema-aware persistent settings backed by the plugin's separate mutable data root.</summary>
public sealed class PluginSettingsStore : IPluginSettings
{
    private readonly object gate = new object();
    private readonly string path;
    private Dictionary<string, string> values;
    private readonly Dictionary<string, Func<object?, bool>> validators = new Dictionary<string, Func<object?, bool>>(StringComparer.Ordinal);
    private readonly IPluginResourceScope? resources;
    private const string SchemaKey = "__alacrity.schema";

    public PluginSettingsStore(string alacrityRoot, PluginId pluginId)
        : this(alacrityRoot, pluginId, 0, null, null)
    {
    }

    /// <summary>Creates settings with an optional host-supplied schema migration.</summary>
    public PluginSettingsStore(string alacrityRoot, PluginId pluginId, int schemaVersion, Action<PluginSettingsStore, int>? migrate)
        : this(alacrityRoot, pluginId, schemaVersion, migrate, null)
    {
    }

    internal PluginSettingsStore(string alacrityRoot, PluginId pluginId, IPluginResourceScope resources)
        : this(alacrityRoot, pluginId, 0, null, resources)
    {
    }

    private PluginSettingsStore(string alacrityRoot, PluginId pluginId, int schemaVersion, Action<PluginSettingsStore, int>? migrate, IPluginResourceScope? resources)
    {
        if (schemaVersion < 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        this.resources = resources;
        path = Path.Combine(Path.GetFullPath(alacrityRoot), "data", "plugins", pluginId.Value, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        values = Load();
        var previousVersion = ReadSchemaVersion();
        if (previousVersion < schemaVersion)
        {
            migrate?.Invoke(this, previousVersion);
            values[SchemaKey] = Serialize(schemaVersion);
            Persist();
        }
    }

    public event EventHandler<PluginSettingChangedEventArgs>? Changed;
    public IPluginSetting<T> Register<T>(PluginSettingDefinition<T> definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        ValidateKey(definition.Key);
        return new RegisteredSetting<T>(this, definition, resources);
    }
    public T Get<T>(string key, T defaultValue)
    {
        ValidateKey(key);
        lock (gate)
        {
            if (!values.TryGetValue(key, out var encoded)) return defaultValue;
            try { return Deserialize<T>(encoded); }
            catch { return defaultValue; }
        }
    }
    public void Set<T>(string key, T value)
    {
        ValidateKey(key);
        if (validators.TryGetValue(key, out var validator) && !validator(value)) throw new ArgumentException("The setting value failed registered validation.", nameof(value));
        string serialized = Serialize(value);
        object? oldValue = null;
        lock (gate)
        {
            if (values.TryGetValue(key, out var old))
            {
                if (string.Equals(old, serialized, StringComparison.Ordinal))
                    return;
                try { oldValue = Deserialize<T>(old); }
                catch { oldValue = null; }
            }
            values[key] = serialized;
            Persist();
        }
        Changed?.Invoke(this, new PluginSettingChangedEventArgs(key, oldValue, value));
    }
    public bool Remove(string key)
    {
        ValidateKey(key);
        lock (gate)
        {
            if (!values.Remove(key)) return false;
            Persist();
        }
        Changed?.Invoke(this, new PluginSettingChangedEventArgs(key, null, null));
        return true;
    }
    public void ResetToDefaults()
    {
        lock (gate) { values.Clear(); Persist(); }
    }

    /// <summary>Current host-managed settings schema version.</summary>
    public int SchemaVersion { get { lock (gate) return ReadSchemaVersion(); } }
    /// <summary>Registers a default and optional validator for a settings key.</summary>
    public void Register<T>(string key, T defaultValue, Func<T, bool>? validator = null)
    {
        ValidateKey(key);
        if (validator != null) validators[key] = value => value is T typed && validator(typed);
        if (!values.ContainsKey(key)) Set(key, defaultValue);
    }
    /// <summary>Creates an isolated key namespace for one internal plugin feature.</summary>
    public IPluginSettings CreateFeatureSettings(PluginFeatureId featureId)
    {
        if (string.IsNullOrWhiteSpace(featureId.Value)) throw new ArgumentException("A feature ID is required.", nameof(featureId));
        return new PrefixedSettings(this, "feature." + featureId.Value + ".");
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using (var stream = File.OpenRead(path))
                return (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>)).ReadObject(stream)!;
        }
        catch { return new Dictionary<string, string>(StringComparer.Ordinal); }
    }
    private void Persist()
    {
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Dictionary<string, string>)).WriteObject(stream, values);
        if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
    }
    private int ReadSchemaVersion()
    {
        try { return values.TryGetValue(SchemaKey, out var value) ? Deserialize<int>(value) : 0; }
        catch { return 0; }
    }
    private void ResetPrefix(string prefix)
    {
        lock (gate)
        {
            foreach (var key in values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) values.Remove(key);
            Persist();
        }
    }
    private static string Serialize<T>(T value)
    {
        using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return Convert.ToBase64String(stream.ToArray()); }
    }
    private static T Deserialize<T>(string value)
    {
        using (var stream = new MemoryStream(Convert.FromBase64String(value))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!;
    }
    private static void ValidateKey(string key) { if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '/', '\\' }) >= 0) throw new ArgumentException("A plugin setting key is required and cannot contain a path separator.", nameof(key)); }

    private sealed class PrefixedSettings : IPluginSettings
    {
        private readonly PluginSettingsStore store; private readonly string prefix;
        public PrefixedSettings(PluginSettingsStore store, string prefix) { this.store = store; this.prefix = prefix; }
        public event EventHandler<PluginSettingChangedEventArgs>? Changed { add { store.Changed += value; } remove { store.Changed -= value; } }
        public IPluginSetting<T> Register<T>(PluginSettingDefinition<T> definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return new PrefixedSetting<T>(store.Register(new PluginSettingDefinition<T>(prefix + definition.Key, definition.DefaultValue, definition.Normalize)), prefix);
        }
        public T Get<T>(string key, T defaultValue) => store.Get(prefix + key, defaultValue);
        public void Set<T>(string key, T value) => store.Set(prefix + key, value);
        public bool Remove(string key) => store.Remove(prefix + key);
        public void ResetToDefaults() => store.ResetPrefix(prefix);
    }

    private sealed class RegisteredSetting<T> : IPluginSetting<T>
    {
        private readonly PluginSettingsStore store;
        private readonly PluginSettingDefinition<T> definition;
        private readonly IPluginResourceScope? resources;

        public RegisteredSetting(PluginSettingsStore store, PluginSettingDefinition<T> definition, IPluginResourceScope? resources)
        {
            this.store = store;
            this.definition = definition;
            this.resources = resources;
        }

        public string Key => definition.Key;
        public T DefaultValue => definition.DefaultValue;
        public T Value
        {
            get { return Normalize(store.Get(definition.Key, definition.DefaultValue)); }
            set { store.Set(definition.Key, Normalize(value)); }
        }

        public void Reset() { Value = definition.DefaultValue; }

        public IPluginRegistration Subscribe(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var registration = new SettingSubscription<T>(store, definition, handler);
            if (resources != null) resources.Own("setting-subscription:" + definition.Key, PluginResourceKind.Configuration, registration);
            return registration;
        }

        private T Normalize(T value) => definition.Normalize == null ? value : definition.Normalize(value);
    }

    private sealed class PrefixedSetting<T> : IPluginSetting<T>
    {
        private readonly IPluginSetting<T> inner;
        private readonly string prefix;
        public PrefixedSetting(IPluginSetting<T> inner, string prefix) { this.inner = inner; this.prefix = prefix; }
        public string Key => inner.Key.Substring(prefix.Length);
        public T DefaultValue => inner.DefaultValue;
        public T Value { get => inner.Value; set => inner.Value = value; }
        public void Reset() => inner.Reset();
        public IPluginRegistration Subscribe(Action<T> handler) => inner.Subscribe(handler);
    }

    private sealed class SettingSubscription<T> : IPluginRegistration
    {
        private readonly PluginSettingsStore store;
        private readonly PluginSettingDefinition<T> definition;
        private readonly Action<T> handler;
        private bool released;

        public SettingSubscription(PluginSettingsStore store, PluginSettingDefinition<T> definition, Action<T> handler)
        {
            this.store = store;
            this.definition = definition;
            this.handler = handler;
            store.Changed += OnChanged;
        }

        public string Name => "setting-subscription:" + definition.Key;
        public bool IsReleased => released;
        public void Dispose()
        {
            if (released) return;
            released = true;
            store.Changed -= OnChanged;
        }

        private void OnChanged(object? sender, PluginSettingChangedEventArgs args)
        {
            if (released || !string.Equals(args.Key, definition.Key, StringComparison.Ordinal)) return;
            handler(args.NewValue is T value ? Normalize(value) : definition.DefaultValue);
        }

        private T Normalize(T value) => definition.Normalize == null ? value : definition.Normalize(value);
    }
}
