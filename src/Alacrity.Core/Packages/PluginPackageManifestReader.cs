using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Reads package metadata before a plugin assembly is considered for loading.
/// This reader deliberately has no assembly-loading dependency: package identity and permissions
/// are established from <c>plugin.json</c>, not from executable plugin code.
/// </summary>
public sealed class PluginPackageManifestReader
{
    /// <summary>The currently supported package manifest schema.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Reads and validates the authoritative manifest in a package directory.</summary>
    public PluginManifest ReadFromPackage(string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory))
            throw new ArgumentException("A package directory is required.", nameof(packageDirectory));

        return ReadFromFile(Path.Combine(Path.GetFullPath(packageDirectory), "plugin.json"));
    }

    /// <summary>Reads and validates an authoritative package manifest file.</summary>
    public PluginManifest ReadFromFile(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("A plugin manifest path is required.", nameof(manifestPath));
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The package does not contain plugin.json.", manifestPath);

        PackageManifestDocument document;
        try
        {
            using (var stream = File.OpenRead(manifestPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(PackageManifestDocument));
                document = (PackageManifestDocument)serializer.ReadObject(stream)!;
            }
        }
        catch (Exception exception) when (exception is SerializationException || exception is InvalidDataException)
        {
            throw new InvalidDataException("plugin.json is not a valid Alacrity package manifest.", exception);
        }

        return BuildManifest(document);
    }

    private static PluginManifest BuildManifest(PackageManifestDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported plugin manifest schema version: " + document.SchemaVersion + ".");

        if (!PluginId.TryParse(document.Id, out var id))
            throw new InvalidDataException("plugin.json contains an invalid plugin ID.");
        if (!Version.TryParse(document.Version, out var version))
            throw new InvalidDataException("plugin.json contains an invalid plugin version.");
        if (!TryParseEnum(document.MultiplayerSafety, MultiplayerSafety.ClientOnly, out MultiplayerSafety multiplayerSafety))
            throw new InvalidDataException("plugin.json contains an invalid multiplayer safety value.");

        var dependencies = new List<PluginDependency>();
        foreach (var dependency in document.Dependencies ?? Array.Empty<PackageDependencyDocument>())
        {
            if (!PluginId.TryParse(dependency.Id, out var dependencyId))
                throw new InvalidDataException("plugin.json contains an invalid dependency ID.");

            Version? minimumVersion = null;
            if (!string.IsNullOrWhiteSpace(dependency.MinimumVersion) &&
                !Version.TryParse(dependency.MinimumVersion, out minimumVersion))
                throw new InvalidDataException("plugin.json contains an invalid dependency version.");

            dependencies.Add(new PluginDependency(dependencyId, minimumVersion));
        }

        var manifest = new PluginManifest(
            id,
            document.Name ?? string.Empty,
            version,
            document.Publisher ?? string.Empty,
            document.Description ?? string.Empty,
            document.SupportedGameVersions ?? Array.Empty<string>(),
            dependencies,
            ParseFlags<PluginCapability>(document.Capabilities, "capability"),
            ParseFlags<PluginPermission>(document.Permissions, "permission"),
            multiplayerSafety,
            document.RequiresServerSupport,
            changelog: document.Changelog,
            entryAssembly: document.EntryAssembly,
            entryType: document.EntryType,
            compatibility: document.PluginSdkCompatibilityVersion == 0 && document.HostCompatibilityVersion == 0 && document.BridgeAbiVersion == 0
                ? PluginCompatibilityRequirements.Legacy
                : new PluginCompatibilityRequirements(document.PluginSdkCompatibilityVersion, document.HostCompatibilityVersion, document.BridgeAbiVersion));
        manifest.Validate();
        return manifest;
    }

    private static bool TryParseEnum<TEnum>(string? value, TEnum defaultValue, out TEnum parsed)
        where TEnum : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = defaultValue;
            return true;
        }

        return Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(TEnum), parsed);
    }

    private static TEnum ParseFlags<TEnum>(string[]? values, string valueKind)
        where TEnum : struct
    {
        long combined = 0;
        foreach (var value in values ?? Array.Empty<string>())
        {
            if (!Enum.TryParse(value, true, out TEnum parsed) || !Enum.IsDefined(typeof(TEnum), parsed))
                throw new InvalidDataException("plugin.json contains an invalid " + valueKind + ": " + value + ".");
            combined |= Convert.ToInt64(parsed);
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), combined);
    }

    [DataContract]
    private sealed class PackageManifestDocument
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "id", IsRequired = true)] public string? Id { get; set; }
        [DataMember(Name = "name", IsRequired = true)] public string? Name { get; set; }
        [DataMember(Name = "version", IsRequired = true)] public string? Version { get; set; }
        [DataMember(Name = "publisher", IsRequired = true)] public string? Publisher { get; set; }
        [DataMember(Name = "description", IsRequired = true)] public string? Description { get; set; }
        [DataMember(Name = "supportedGameVersions", IsRequired = true)] public string[]? SupportedGameVersions { get; set; }
        [DataMember(Name = "dependencies")] public PackageDependencyDocument[]? Dependencies { get; set; }
        [DataMember(Name = "capabilities")] public string[]? Capabilities { get; set; }
        [DataMember(Name = "permissions")] public string[]? Permissions { get; set; }
        [DataMember(Name = "multiplayerSafety")] public string? MultiplayerSafety { get; set; }
        [DataMember(Name = "requiresServerSupport")] public bool RequiresServerSupport { get; set; }
        [DataMember(Name = "changelog")] public string? Changelog { get; set; }
        [DataMember(Name = "entryAssembly")] public string? EntryAssembly { get; set; }
        [DataMember(Name = "entryType")] public string? EntryType { get; set; }
        [DataMember(Name = "pluginSdkCompatibilityVersion")] public int PluginSdkCompatibilityVersion { get; set; }
        [DataMember(Name = "hostCompatibilityVersion")] public int HostCompatibilityVersion { get; set; }
        [DataMember(Name = "bridgeAbiVersion")] public int BridgeAbiVersion { get; set; }
    }

    [DataContract]
    private sealed class PackageDependencyDocument
    {
        [DataMember(Name = "id", IsRequired = true)] public string? Id { get; set; }
        [DataMember(Name = "minimumVersion")] public string? MinimumVersion { get; set; }
    }
}
