using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// <summary>Stable identifier for a feature owned by one plugin package.</summary>
public readonly struct PluginFeatureId : IEquatable<PluginFeatureId>
{
    /// <summary>Creates a stable feature identifier.</summary>
    public PluginFeatureId(string value) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A feature ID is required.", nameof(value)); Value = value; }
    /// <summary>Canonical feature identifier text.</summary>
    public string Value { get; }
    /// <summary>Compares two feature identifiers ordinally.</summary>
    public bool Equals(PluginFeatureId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PluginFeatureId other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
}

/// <summary>Immutable metadata for a runtime-toggleable internal plugin feature.</summary>
public sealed class PluginFeatureMetadata
{
    /// <summary>Creates metadata for an independently toggleable feature.</summary>
    public PluginFeatureMetadata(PluginFeatureId id, string displayName, bool canDisableAtRuntime = true) { Id = id; DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName; CanDisableAtRuntime = canDisableAtRuntime; }
    /// <summary>Stable feature identity within the package.</summary>
    public PluginFeatureId Id { get; }
    /// <summary>Human-readable feature name.</summary>
    public string DisplayName { get; }
    /// <summary>Whether the host may disable this feature without restarting.</summary>
    public bool CanDisableAtRuntime { get; }
}

/// <summary>Independently toggleable unit inside a plugin package.</summary>
public interface IPluginFeature
{
    /// <summary>Immutable metadata describing this feature.</summary>
    PluginFeatureMetadata Metadata { get; }
    /// <summary>Enables the feature with a dedicated child resource scope.</summary>
    Task EnableAsync(IPluginFeatureContext context, CancellationToken cancellationToken);
    /// <summary>Disables feature work before the host releases its child scope.</summary>
    Task DisableAsync(CancellationToken cancellationToken);
}

/// <summary>Host-owned child scope and logger supplied to one feature activation.</summary>
public interface IPluginFeatureContext
{
    /// <summary>Verified owning plugin manifest.</summary>
    PluginManifest Plugin { get; }
    /// <summary>Metadata for the feature being enabled.</summary>
    PluginFeatureMetadata Feature { get; }
    /// <summary>Child resource scope released when the feature disables.</summary>
    IPluginResourceScope Resources { get; }
    /// <summary>Plugin-scoped host logger.</summary>
    IPluginLogger Logger { get; }
}
