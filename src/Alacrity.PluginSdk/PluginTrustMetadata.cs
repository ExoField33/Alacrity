using System;

namespace Alacrity.PluginSdk;

/// <summary>Host-computed package trust state. A package claim cannot set this value.</summary>
public enum PluginTrustLevel
{
    /// <summary>Officially distributed by Alacrity.</summary>
    Official,
    /// <summary>Verified third-party publisher.</summary>
    VerifiedThirdParty,
    /// <summary>Explicitly trusted by the local user.</summary>
    LocallyTrusted,
    /// <summary>Not verified by the host.</summary>
    Unverified,
    /// <summary>Package contents differ from a verified release.</summary>
    Modified,
    /// <summary>Trust material has expired.</summary>
    Expired,
    /// <summary>Trust material has been revoked.</summary>
    Revoked
}

/// <summary>Publisher and package-integrity claims supplied with a plugin package.</summary>
public sealed class PluginTrustMetadata
{
    /// <summary>Creates metadata without performing cryptographic verification.</summary>
    public PluginTrustMetadata(
        string publisherId,
        string packageSha256,
        string? signatureAlgorithm = null,
        string? signature = null)
    {
        PublisherId = RequireText(publisherId, nameof(publisherId));
        PackageSha256 = RequireText(packageSha256, nameof(packageSha256));
        SignatureAlgorithm = signatureAlgorithm;
        Signature = signature;
    }

    /// <summary>Stable publisher identity used by policy matching.</summary>
    public string PublisherId { get; }
    /// <summary>Lowercase SHA-256 digest of the complete package.</summary>
    public string PackageSha256 { get; }
    /// <summary>Signature scheme identifier, when signed.</summary>
    public string? SignatureAlgorithm { get; }
    /// <summary>Encoded package signature, when signed.</summary>
    public string? Signature { get; }

    /// <summary>Whether both signature fields are present.</summary>
    public bool IsSigned => !string.IsNullOrWhiteSpace(SignatureAlgorithm) && !string.IsNullOrWhiteSpace(Signature);

    /// <summary>Validates metadata shape; cryptographic verification belongs to the host.</summary>
    public void Validate()
    {
        if (PackageSha256.Length != 64)
            throw new InvalidOperationException("PackageSha256 must contain 64 hexadecimal characters.");

        for (var i = 0; i < PackageSha256.Length; i++)
        {
            var character = PackageSha256[i];
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F')))
                throw new InvalidOperationException("PackageSha256 must contain hexadecimal characters only.");
        }

        var hasAlgorithm = !string.IsNullOrWhiteSpace(SignatureAlgorithm);
        var hasSignature = !string.IsNullOrWhiteSpace(Signature);
        if (hasAlgorithm != hasSignature)
            throw new InvalidOperationException("SignatureAlgorithm and Signature must be supplied together.");
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty value is required.", parameterName);
        return value;
    }
}
