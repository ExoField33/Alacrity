using System;
using System.IO;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Creates and exposes the host-owned on-disk boundary for one plugin package.</summary>
public sealed class PluginStorage
{
    private readonly string pluginsRoot;

    public PluginStorage(string alacrityRoot)
    {
        if (string.IsNullOrWhiteSpace(alacrityRoot))
            throw new ArgumentException("An Alacrity root is required.", nameof(alacrityRoot));

        AlacrityRoot = Path.GetFullPath(alacrityRoot);
        pluginsRoot = Path.Combine(AlacrityRoot, "Plugins");
    }

    public string AlacrityRoot { get; }

    public PluginStorageLayout EnsureLayout(PluginManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        manifest.Validate();
        Directory.CreateDirectory(pluginsRoot);
        string pluginDirectory = Path.Combine(pluginsRoot, manifest.Id.Value);
        EnsureWithinPluginsRoot(pluginDirectory);
        Directory.CreateDirectory(pluginDirectory);

        string dataDirectory = Path.Combine(pluginDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        string metadataPath = Path.Combine(pluginDirectory, "manifest.json");
        string configPath = Path.Combine(pluginDirectory, "config.json");
        WriteIfAbsent(metadataPath, BuildManifestJson(manifest));
        WriteIfAbsent(configPath, "{}" + Environment.NewLine);
        return new PluginStorageLayout(pluginDirectory, metadataPath, configPath, dataDirectory);
    }

    private void EnsureWithinPluginsRoot(string candidate)
    {
        string rootWithSeparator = pluginsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullCandidate = Path.GetFullPath(candidate);
        if (!fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plugin storage must remain inside Alacrity/Plugins.");
    }

    private static void WriteIfAbsent(string path, string contents)
    {
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
                writer.Write(contents);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Existing user configuration and package metadata are never overwritten by setup.
        }
    }

    private static string BuildManifestJson(PluginManifest manifest)
    {
        return "{\n" +
               "  \"id\": \"" + Escape(manifest.Id.Value) + "\",\n" +
               "  \"name\": \"" + Escape(manifest.Name) + "\",\n" +
               "  \"version\": \"" + Escape(manifest.Version.ToString()) + "\",\n" +
               "  \"publisher\": \"" + Escape(manifest.Publisher) + "\"\n" +
               "}\n";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}

public sealed class PluginStorageLayout
{
    public PluginStorageLayout(string pluginDirectory, string metadataPath, string configPath, string dataDirectory)
    {
        PluginDirectory = pluginDirectory;
        MetadataPath = metadataPath;
        ConfigPath = configPath;
        DataDirectory = dataDirectory;
    }

    public string PluginDirectory { get; }
    public string MetadataPath { get; }
    public string ConfigPath { get; }
    public string DataDirectory { get; }
}
