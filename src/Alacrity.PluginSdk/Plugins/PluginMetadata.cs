using System;
using System.Collections.Generic;
using System.Linq;

namespace Alacrity.PluginSdk;

/// <summary>Capabilities a plugin may declare for policy and host validation.</summary>
[Flags]
public enum PluginCapability
{
    /// <summary>No additional capability.</summary>
    None = 0,
    /// <summary>Creates or extends user-interface elements.</summary>
    UserInterface = 1,
    /// <summary>Participates in rendering extension points.</summary>
    Rendering = 2,
    /// <summary>Observes or extends client input.</summary>
    Input = 4,
    /// <summary>Uses audio extension points.</summary>
    Audio = 8,
    /// <summary>Reads host-provided game snapshots.</summary>
    GameStateRead = 16,
    /// <summary>Observes multiplayer state.</summary>
    MultiplayerObservation = 32,
    /// <summary>Uses explicitly approved networking services.</summary>
    Networking = 64,
    /// <summary>Provides diagnostic output.</summary>
    Diagnostics = 128
}

/// <summary>Host-mediated permissions requested by a plugin.</summary>
[Flags]
public enum PluginPermission
{
    /// <summary>No host permission.</summary>
    None = 0,
    /// <summary>Read-only game-state access.</summary>
    ReadGameState = 1,
    /// <summary>Draw-only user-interface access.</summary>
    DrawUserInterface = 2,
    /// <summary>Read-only multiplayer observation.</summary>
    ObserveMultiplayer = 4,
    /// <summary>May request opening user-approved external links.</summary>
    OpenExternalLinks = 8,
    /// <summary>May request clipboard access.</summary>
    Clipboard = 16,
    /// <summary>May request local network access.</summary>
    LocalNetwork = 32,
    /// <summary>May request managed plugin-data file access.</summary>
    FileSystem = 64,
    /// <summary>May request host-managed full-file patch registration.</summary>
    ManagedPatch = 128
}

/// <summary>Describes how a plugin behaves in multiplayer.</summary>
public enum MultiplayerSafety
{
    /// <summary>Runs entirely on the local client.</summary>
    ClientOnly,
    /// <summary>Changes pixels or local presentation only.</summary>
    ClientPresentationOnly,
    /// <summary>Observes client-visible multiplayer state.</summary>
    ClientObservation,
    /// <summary>Uses optional capabilities from an Alacrity-aware server.</summary>
    ServerCooperative,
    /// <summary>Cannot operate without explicit server support.</summary>
    ServerRequired,
    /// <summary>Requires explicit policy review before activation.</summary>
    Restricted
}

/// <summary>Dependency on another plugin and, optionally, its minimum version.</summary>
public sealed class PluginDependency
{
    /// <summary>Creates a plugin dependency declaration.</summary>
    public PluginDependency(PluginId id, Version? minimumVersion = null)
    {
        Id = id;
        MinimumVersion = minimumVersion;
    }

    /// <summary>Required plugin identifier.</summary>
    public PluginId Id { get; }

    /// <summary>Minimum compatible version, when one is required.</summary>
    public Version? MinimumVersion { get; }
}

/// <summary>Immutable metadata and declared capabilities for a plugin package.</summary>
public sealed class PluginManifest
{
    /// <summary>Creates a manifest without loading or executing plugin code.</summary>
    public PluginManifest(
        PluginId id,
        string name,
        Version version,
        string publisher,
        string description,
        IEnumerable<string> supportedGameVersions,
        IEnumerable<PluginDependency>? dependencies = null,
        PluginCapability capabilities = PluginCapability.None,
        PluginPermission permissions = PluginPermission.None,
        MultiplayerSafety multiplayerSafety = MultiplayerSafety.ClientOnly,
        bool requiresServerSupport = false,
        PluginTrustMetadata? trust = null,
        string? changelog = null,
        string? entryAssembly = null,
        string? entryType = null,
        PluginCompatibilityRequirements? compatibility = null)
    {
        Id = id;
        Name = RequireText(name, nameof(name));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Publisher = RequireText(publisher, nameof(publisher));
        Description = RequireText(description, nameof(description));
        SupportedGameVersions = CopyRequired(supportedGameVersions, nameof(supportedGameVersions));
        Dependencies = CopyOptional(dependencies);
        Capabilities = capabilities;
        Permissions = permissions;
        MultiplayerSafety = multiplayerSafety;
        RequiresServerSupport = requiresServerSupport;
        Trust = trust;
        Changelog = string.IsNullOrWhiteSpace(changelog) ? "No changelog is available." : changelog!;
        EntryAssembly = string.IsNullOrWhiteSpace(entryAssembly) ? null : entryAssembly;
        EntryType = string.IsNullOrWhiteSpace(entryType) ? null : entryType;
        Compatibility = compatibility ?? PluginCompatibilityRequirements.Current;
    }

    /// <summary>Stable plugin identifier.</summary>
    public PluginId Id { get; }
    /// <summary>Human-readable plugin name.</summary>
    public string Name { get; }
    /// <summary>Plugin package version.</summary>
    public Version Version { get; }
    /// <summary>Publisher identity displayed to users and policy tooling.</summary>
    public string Publisher { get; }
    /// <summary>Human-readable capability description.</summary>
    public string Description { get; }
    /// <summary>Game versions this package declares as compatible.</summary>
    public IReadOnlyList<string> SupportedGameVersions { get; }
    /// <summary>Other plugins required by this package.</summary>
    public IReadOnlyList<PluginDependency> Dependencies { get; }
    /// <summary>Declared host capabilities.</summary>
    public PluginCapability Capabilities { get; }
    /// <summary>Declared host permissions.</summary>
    public PluginPermission Permissions { get; }
    /// <summary>Declared multiplayer safety classification.</summary>
    public MultiplayerSafety MultiplayerSafety { get; }
    /// <summary>Whether the plugin requires an Alacrity-aware server.</summary>
    public bool RequiresServerSupport { get; }
    /// <summary>Publisher and package-integrity claims, if supplied.</summary>
    public PluginTrustMetadata? Trust { get; }
    /// <summary>Human-readable release notes supplied by the plugin package.</summary>
    public string Changelog { get; }
    /// <summary>Package-relative entry assembly loaded only after host validation.</summary>
    public string? EntryAssembly { get; }
    /// <summary>Fully qualified plugin type in the entry assembly.</summary>
    public string? EntryType { get; }
    /// <summary>SDK, host, and bridge requirements checked before the entry assembly loads.</summary>
    public PluginCompatibilityRequirements Compatibility { get; }

    /// <summary>Validates cross-field manifest invariants.</summary>
    public void Validate()
    {
        if (SupportedGameVersions.Count == 0)
            throw new InvalidOperationException("A plugin must declare at least one supported game version.");

        if (RequiresServerSupport && MultiplayerSafety != MultiplayerSafety.ServerRequired &&
            MultiplayerSafety != MultiplayerSafety.ServerCooperative)
            throw new InvalidOperationException("Server support requires a server-aware multiplayer classification.");

        if (MultiplayerSafety == MultiplayerSafety.Restricted && RequiresServerSupport)
            throw new InvalidOperationException("Restricted plugins cannot require server support by default.");

        Trust?.Validate();

        if ((EntryAssembly == null) != (EntryType == null))
            throw new InvalidOperationException("EntryAssembly and EntryType must be declared together.");
        if (EntryAssembly != null && (System.IO.Path.IsPathRooted(EntryAssembly) || EntryAssembly.IndexOf("..", StringComparison.Ordinal) >= 0))
            throw new InvalidOperationException("EntryAssembly must be a package-relative path.");
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty value is required.", parameterName);
        return value;
    }

    private static IReadOnlyList<string> CopyRequired(IEnumerable<string> values, string parameterName)
    {
        if (values == null)
            throw new ArgumentNullException(parameterName);

        var copy = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("At least one non-empty value is required.", parameterName);
        return copy;
    }

    private static IReadOnlyList<PluginDependency> CopyOptional(IEnumerable<PluginDependency>? values)
    {
        return (values ?? Enumerable.Empty<PluginDependency>()).ToArray();
    }
}
