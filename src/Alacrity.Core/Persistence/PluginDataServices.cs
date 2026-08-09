using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
    private readonly StorageScopeGuard? scopeGuard;

    public PluginDataStore(string alacrityRoot, PluginId pluginId)
        : this(alacrityRoot, pluginId, null)
    {
    }

    internal PluginDataStore(string alacrityRoot, PluginId pluginId, IPluginResourceScope? resources)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot)) throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));
        if (!pluginId.IsValid) throw new ArgumentException("A valid plugin ID is required.", nameof(pluginId));
        root = Path.Combine(Path.GetFullPath(alacrityRoot), "data", "plugins", pluginId.Value, "user-data");
        Directory.CreateDirectory(root);
        EnsureNotReparsePoint(root);
        if (resources != null)
        {
            scopeGuard = new StorageScopeGuard();
            try { resources.Own("plugin-storage", PluginResourceKind.Asset, scopeGuard); }
            catch { scopeGuard.Dispose(); throw; }
        }
    }

    /// <summary>Absolute plugin-confined mutable data path.</summary>
    public string RootPath => root;
    public Stream OpenRead(string relativePath) { EnsureActive(); return new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read); }
    public Stream Create(string relativePath)
    {
        EnsureActive();
        var path = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }
    public bool Exists(string relativePath) { EnsureActive(); return File.Exists(Resolve(relativePath)); }
    public void Delete(string relativePath)
    {
        EnsureActive();
        var path = Resolve(relativePath);
        if (File.Exists(path)) File.Delete(path);
    }
    public IReadOnlyList<string> Enumerate(string relativeDirectory)
    {
        EnsureActive();
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
        EnsureActive();
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
    private void EnsureActive()
    {
        if (scopeGuard != null && scopeGuard.IsReleased) throw new ObjectDisposedException("IPluginStorage", "The owning plugin scope has been released.");
    }
    private sealed class StorageScopeGuard : IDisposable
    {
        private int released;
        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
        public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); }
    }
}

/// <summary>Schema-aware persistent settings backed by the plugin's separate mutable data root.</summary>
public sealed class PluginSettingsStore : IPluginSettings
{
    // Contexts from an old and a new activation can overlap briefly during lifecycle teardown.
    // Serialize writes by destination so those independent stores cannot race File.Replace.
    private static readonly ConcurrentDictionary<string, object> WriteGates = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new object();
    private readonly string path;
    private Dictionary<string, string> values;
    private readonly Dictionary<string, Func<object?, bool>> validators = new Dictionary<string, Func<object?, bool>>(StringComparer.Ordinal);
    private readonly Dictionary<string, IRegisteredSettingDefinition> definitions = new Dictionary<string, IRegisteredSettingDefinition>(StringComparer.Ordinal);
    private readonly IPluginResourceScope? resources;
    private readonly ScopeGuard? scopeGuard;
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
        if (resources != null)
        {
            scopeGuard = new ScopeGuard();
            try { resources.Own("settings", PluginResourceKind.Configuration, scopeGuard); }
            catch { scopeGuard.Dispose(); throw; }
        }
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
        EnsureScopeActive();
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        ValidateKey(definition.Key);
        var registeredDefinition = new RegisteredSettingDefinition<T>(definition);
        lock (gate)
        {
            if (definitions.TryGetValue(definition.Key, out IRegisteredSettingDefinition existing))
            {
                if (!existing.IsCompatibleWith(registeredDefinition))
                    throw new InvalidOperationException("The setting '" + definition.Key + "' was registered with a distinct or incompatible definition. Reuse the same definition instance when shared access is intended.");
            }
            else
            {
                definitions.Add(definition.Key, registeredDefinition);
            }
        }
        return new RegisteredSetting<T>(this, definition, resources);
    }
    public T Get<T>(string key, T defaultValue)
    {
        EnsureScopeActive();
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
        EnsureScopeActive();
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
        EnsureScopeActive();
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
        EnsureScopeActive();
        ResetRegisteredDefaults(null);
    }

    /// <summary>Current host-managed settings schema version.</summary>
    public int SchemaVersion { get { lock (gate) return ReadSchemaVersion(); } }
    /// <summary>Registers a default and optional validator for a settings key.</summary>
    public void Register<T>(string key, T defaultValue, Func<T, bool>? validator = null)
    {
        EnsureScopeActive();
        ValidateKey(key);
        if (validator != null) validators[key] = value => value is T typed && validator(typed);
        if (!values.ContainsKey(key)) Set(key, defaultValue);
    }
    /// <summary>Creates an isolated key namespace for one internal plugin feature.</summary>
    public IPluginSettings CreateFeatureSettings(PluginFeatureId featureId)
    {
        EnsureScopeActive();
        if (string.IsNullOrWhiteSpace(featureId.Value)) throw new ArgumentException("A feature ID is required.", nameof(featureId));
        return new PrefixedSettings(this, "feature." + featureId.Value + ".");
    }

    private Dictionary<string, string> Load()
    {
        object writeGate = WriteGates.GetOrAdd(path, _ => new object());
        lock (writeGate)
        {
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                // A newly created activation can read settings while an older activation is still
                // completing its final write. Share deletion explicitly and take the same path lock
                // as Persist so the atomic replace never fails because of our own reader.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return (Dictionary<string, string>)new DataContractJsonSerializer(typeof(Dictionary<string, string>)).ReadObject(stream)!;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
    }
    private void Persist()
    {
        object writeGate = WriteGates.GetOrAdd(path, _ => new object());
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (writeGate)
        {
            try
            {
                using (var stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(Dictionary<string, string>)).WriteObject(stream, values);

                ReplaceOrMove(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static void ReplaceOrMove(string temporary, string destination)
    {
        const int maximumAttempts = 4;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null);
                else
                    File.Move(temporary, destination);
                return;
            }
            catch (IOException) when (attempt + 1 < maximumAttempts)
            {
                System.Threading.Thread.Sleep(10 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt + 1 < maximumAttempts)
            {
                System.Threading.Thread.Sleep(10 * (attempt + 1));
            }
        }
    }
    private int ReadSchemaVersion()
    {
        try { return values.TryGetValue(SchemaKey, out var value) ? Deserialize<int>(value) : 0; }
        catch { return 0; }
    }
    private void ResetPrefix(string prefix)
    {
        EnsureScopeActive();
        ResetRegisteredDefaults(prefix);
    }

    private void ResetRegisteredDefaults(string? prefix)
    {
        var changes = new List<PluginSettingChangedEventArgs>();
        lock (gate)
        {
            foreach (var pair in definitions)
            {
                if (prefix != null && !pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                IRegisteredSettingDefinition definition = pair.Value;
                string replacement = definition.SerializedDefault;
                if (values.TryGetValue(pair.Key, out string existing) && string.Equals(existing, replacement, StringComparison.Ordinal))
                    continue;

                object? oldValue = null;
                if (values.TryGetValue(pair.Key, out existing))
                    oldValue = definition.TryDeserialize(existing);
                values[pair.Key] = replacement;
                changes.Add(new PluginSettingChangedEventArgs(pair.Key, oldValue, definition.DefaultValue));
            }

            if (changes.Count != 0)
                Persist();
        }

        // Subscribers are plugin code. They must never run while the persistent settings lock is held.
        for (int index = 0; index < changes.Count; index++)
            Changed?.Invoke(this, changes[index]);
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

    private void EnsureScopeActive()
    {
        if (scopeGuard != null && scopeGuard.IsReleased)
            throw new ObjectDisposedException("IPluginSettings", "The owning plugin scope has been released.");
    }

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
            try
            {
                if (resources != null) resources.Own("setting-subscription:" + definition.Key, PluginResourceKind.Configuration, registration);
            }
            catch
            {
                registration.Dispose();
                throw;
            }
            return registration;
        }

        private T Normalize(T value) => definition.Normalize == null ? value : definition.Normalize(value);
    }

    private interface IRegisteredSettingDefinition
    {
        string SerializedDefault { get; }
        object? DefaultValue { get; }
        bool IsCompatibleWith(IRegisteredSettingDefinition other);
        object? TryDeserialize(string serialized);
    }

    private sealed class RegisteredSettingDefinition<T> : IRegisteredSettingDefinition
    {
        private readonly Type valueType = typeof(T);
        private readonly PluginSettingDefinition<T> source;
        internal RegisteredSettingDefinition(PluginSettingDefinition<T> definition)
        {
            source = definition;
            T normalized = definition.Normalize == null ? definition.DefaultValue : definition.Normalize(definition.DefaultValue);
            Default = normalized;
            SerializedDefault = Serialize(normalized);
        }

        internal T Default { get; }
        public string SerializedDefault { get; }
        public object? DefaultValue => Default;
        public bool IsCompatibleWith(IRegisteredSettingDefinition other)
        {
            var typed = other as RegisteredSettingDefinition<T>;
            return typed != null && typed.valueType == valueType && ReferenceEquals(source, typed.source);
        }
        public object? TryDeserialize(string serialized)
        {
            try { return Deserialize<T>(serialized); }
            catch { return null; }
        }
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released;
        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
        public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); }
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
