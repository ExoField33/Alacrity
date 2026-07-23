using System;

namespace Alacrity.PluginSdk;

/// <summary>Host-computed trust result; package metadata cannot create or override this result.</summary>
public sealed class PluginTrustVerificationResult
{
    /// <summary>Creates a host trust decision after package verification.</summary>
    public PluginTrustVerificationResult(PluginTrustLevel level, string detail)
    {
        Level = level;
        Detail = string.IsNullOrWhiteSpace(detail) ? throw new ArgumentException("A trust detail is required.", nameof(detail)) : detail;
    }
    /// <summary>Effective host-computed trust level.</summary>
    public PluginTrustLevel Level { get; }
    /// <summary>Host diagnostic explaining the decision.</summary>
    public string Detail { get; }
}
