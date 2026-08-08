using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Computes a deterministic protected-file hash; signatures and trust-store policy remain host-owned.</summary>
public sealed class PluginPackageIntegrityVerifier
{
    /// <summary>Verifies protected package files against a hash supplied by a trusted release manifest.</summary>
    public PluginTrustVerificationResult Verify(string packageDirectory, string expectedProtectedHash, PluginTrustLevel verifiedLevel)
    {
        if (string.IsNullOrWhiteSpace(expectedProtectedHash)) throw new ArgumentException("An expected protected-file hash is required.", nameof(expectedProtectedHash));
        var actual = ComputeProtectedHash(packageDirectory);
        if (!string.Equals(actual, expectedProtectedHash, StringComparison.OrdinalIgnoreCase))
            return new PluginTrustVerificationResult(PluginTrustLevel.Modified, "Protected package files do not match the trusted release manifest.");
        return new PluginTrustVerificationResult(verifiedLevel, "Protected package files match the trusted release manifest.");
    }
    /// <summary>Hashes sorted package files excluding mutable signature/release metadata to avoid self-referential hashes.</summary>
    public string ComputeProtectedHash(string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory)) throw new DirectoryNotFoundException("A package directory is required.");
        using (var hash = SHA256.Create())
        {
            foreach (var file in Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = file.Substring(Path.GetFullPath(packageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (string.Equals(relative, "plugin-certificate.json", StringComparison.OrdinalIgnoreCase) || string.Equals(relative, "release-manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Encoding.UTF8.GetBytes(relative + "\n");
                hash.TransformBlock(name, 0, name.Length, name, 0);
                var bytes = File.ReadAllBytes(file);
                hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(hash.Hash!).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
