using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.Persistence;

/// <summary>
/// Owns the small host-managed enabled-plugin state file and its legacy text-file migration.
/// Package discovery and rendering remain elsewhere; this type only translates persisted desired
/// state into package IDs and atomically writes the current package lifecycle state.
/// </summary>
internal sealed class TerrariaPluginEnabledStateStore
{
    private readonly string statePath;
    private readonly string legacyPath;
    private bool restored;

    internal TerrariaPluginEnabledStateStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A client root is required.", nameof(root));
        statePath = Path.Combine(root, "data", "plugin-state.json");
        legacyPath = Path.Combine(root, "data", "enabled-plugins.txt");
    }

    internal void RestoreOnce(PluginManagerRuntime runtime, Action<PluginId> enable, Action<string> reportFailure)
    {
        if (restored) return;
        restored = true;
        try
        {
            HashSet<string> requested = ReadEnabledIds();
            foreach (PluginPackageRuntimeRecord record in runtime.Registry.Records.Where(record =>
                         requested.Contains(record.Manifest.Id.Value) &&
                         (record.State == PluginPackageLifecycleState.Loaded || record.State == PluginPackageLifecycleState.Disabled)).ToArray())
                enable(record.Manifest.Id);
            if (!File.Exists(statePath) && File.Exists(legacyPath))
                Persist(runtime, reportFailure);
        }
        catch (Exception exception)
        {
            reportFailure("Unable to restore enabled plugins: " + exception.Message);
        }
    }

    internal void Persist(PluginManagerRuntime runtime, Action<string> reportFailure)
    {
        try
        {
            string directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            PluginPackageRuntimeRecord[] records = runtime.Registry.Records
                .Where(record => record.State != PluginPackageLifecycleState.Uninstalled)
                .OrderBy(record => record.Manifest.Id.Value, StringComparer.Ordinal)
                .ToArray();
            string json = "{\n  \"plugins\": [\n" + string.Join(",\n", records.Select(record => "    { \"id\": \"" + record.Manifest.Id.Value + "\", \"enabled\": " + (record.State == PluginPackageLifecycleState.Enabled ? "true" : "false") + " }")) + "\n  ]\n}\n";
            string temporaryPath = statePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(statePath)) File.Replace(temporaryPath, statePath, null);
            else File.Move(temporaryPath, statePath);
        }
        catch (Exception exception)
        {
            reportFailure("Unable to save enabled plugins: " + exception.Message);
        }
    }

    private HashSet<string> ReadEnabledIds()
    {
        if (File.Exists(statePath))
        {
            string json = File.ReadAllText(statePath);
            return new HashSet<string>(Regex.Matches(json, "\\\"id\\\"\\s*:\\s*\\\"([a-z0-9.-]+)\\\"\\s*,\\s*\\\"enabled\\\"\\s*:\\s*true", RegexOptions.CultureInvariant).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
        }
        return File.Exists(legacyPath)
            ? new HashSet<string>(File.ReadAllLines(legacyPath), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
