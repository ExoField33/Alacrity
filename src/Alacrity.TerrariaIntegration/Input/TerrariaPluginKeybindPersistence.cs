using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Alacrity.PluginSdk;
using Terraria.GameInput;

namespace AlacrityTerraria.Input;

/// <summary>
/// Stores plugin bindings independently of Terraria's mutable input dictionaries. Keys include
/// the active Terraria profile and input mode so profiles cannot overwrite one another.
/// </summary>
internal sealed class TerrariaPluginKeybindPersistence
{
    private readonly object gate = new object();
    private readonly Dictionary<string, List<string>> bindings = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    private readonly string path;
    private readonly Action<string, Exception> reportFailure;
    private bool loaded;
    private bool dirty;
    private int saveQueued;

    internal TerrariaPluginKeybindPersistence(string root, Action<string, Exception> reportFailure)
    {
        path = Path.Combine(root ?? throw new ArgumentNullException(nameof(root)), "data", "plugin-keybinds.dat");
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal List<string> GetBindings(PluginKeybindRegistration keybind, InputMode mode, string profileName)
    {
        EnsureLoaded();
        lock (gate)
        {
            if (!bindings.TryGetValue(GetKey(keybind, mode, profileName), out List<string> saved))
                bindings.TryGetValue(GetLegacyKey(keybind, mode), out saved);
            return saved == null
                ? (mode == InputMode.Keyboard ? new List<string> { keybind.Descriptor.DefaultBinding } : new List<string>())
                : new List<string>(saved);
        }
    }

    internal void Observe(PluginKeybindRegistration keybind, InputMode mode, string profileName, IReadOnlyList<string> current)
    {
        EnsureLoaded();
        string key = GetKey(keybind, mode, profileName);
        lock (gate)
        {
            if (bindings.TryGetValue(key, out List<string> existing) && existing.SequenceEqual(current, StringComparer.Ordinal)) return;
            bindings[key] = new List<string>(current);
            dirty = true;
        }
        QueueSave();
    }

    internal void Prune(ISet<string> activeHostIds)
    {
        EnsureLoaded();
        bool changed = false;
        lock (gate)
        {
            foreach (string key in bindings.Keys.Where(key => !activeHostIds.Any(id => key.EndsWith(":" + id, StringComparison.Ordinal))).ToArray())
            {
                bindings.Remove(key);
                changed = true;
            }
            dirty |= changed;
        }
        if (changed) QueueSave();
    }

    private void EnsureLoaded()
    {
        lock (gate)
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 2) continue;
                    string key = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                    var values = new List<string>();
                    if (!string.IsNullOrEmpty(parts[1]))
                        foreach (string encoded in parts[1].Split(',')) values.Add(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                    bindings[key] = values;
                }
            }
            catch (Exception exception)
            {
                bindings.Clear();
                reportFailure("Plugin keybind persistence load", exception);
            }
        }
    }

    private void QueueSave()
    {
        if (Interlocked.Exchange(ref saveQueued, 1) == 0) ThreadPool.QueueUserWorkItem(_ => Save());
    }

    private void Save()
    {
        Dictionary<string, List<string>> snapshot;
        lock (gate)
        {
            if (!dirty) { Interlocked.Exchange(ref saveQueued, 0); return; }
            snapshot = bindings.ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value), StringComparer.Ordinal);
            dirty = false;
        }
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string[] lines = snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)) + "|" + string.Join(",", pair.Value.Select(value => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))))).ToArray();
            string temporary = path + ".tmp";
            File.WriteAllLines(temporary, lines);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        catch (Exception exception) { reportFailure("Plugin keybind persistence save", exception); }
        finally
        {
            Interlocked.Exchange(ref saveQueued, 0);
            lock (gate) if (dirty) QueueSave();
        }
    }

    private static string GetKey(PluginKeybindRegistration keybind, InputMode mode, string profileName) => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(profileName) ? "default" : profileName)) + ":" + ((int)mode).ToString() + ":" + keybind.HostId;
    private static string GetLegacyKey(PluginKeybindRegistration keybind, InputMode mode) => ((int)mode).ToString() + ":" + keybind.HostId;
}
