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
    private const string SchemaKey = "__alacrity.schema";

    public PluginSettingsStore(string alacrityRoot, PluginId pluginId)
        : this(alacrityRoot, pluginId, 0, null)
    {
    }

    /// <summary>Creates settings with an optional host-supplied schema migration.</summary>
    public PluginSettingsStore(string alacrityRoot, PluginId pluginId, int schemaVersion, Action<PluginSettingsStore, int>? migrate)
    {
        if (schemaVersion < 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
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
        object? oldValue;
        lock (gate)
        {
            oldValue = values.TryGetValue(key, out var old) ? old : null;
            values[key] = Serialize(value);
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
        public T Get<T>(string key, T defaultValue) => store.Get(prefix + key, defaultValue);
        public void Set<T>(string key, T value) => store.Set(prefix + key, value);
        public bool Remove(string key) => store.Remove(prefix + key);
        public void ResetToDefaults() => store.ResetPrefix(prefix);
    }
}
