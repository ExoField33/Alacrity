using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace AlacrityTerraria;

public sealed class ClientManifestIntegrityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "alacrity-client-integrity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidatesTheCompleteDeployedRuntimeSet()
    {
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        string runtime = Path.Combine(directory, "bin", "Alacrity.PluginUiCoreBridge.dll");
        string executable = Path.Combine(directory, "Alacrity.exe");
        File.WriteAllText(runtime, "bridge");
        File.WriteAllText(executable, "client");
        WriteManifest("5|2|14|1.4.5.6", Hash(executable), "bin/Alacrity.PluginUiCoreBridge.dll", Hash(runtime));

        Assert.True(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out string diagnostic), diagnostic);

        File.WriteAllText(runtime, "tampered");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out diagnostic));
        Assert.Contains("failed integrity", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMissingOrIncompatibleClientManifests()
    {
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out _));

        Directory.CreateDirectory(directory);
        WriteManifest("1|2|2|1.4.5.6", "00", "bin/missing.dll", "00");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out string diagnostic));
        Assert.Contains("handshake", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedAndEscapingRuntimeEntries()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "alacrity-client-manifest.json"), "not json");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out string diagnostic));
        Assert.Contains("validation failed", diagnostic, StringComparison.OrdinalIgnoreCase);

        string executable = Path.Combine(directory, "Alacrity.exe");
        File.WriteAllText(executable, "client");
        WriteManifest("5|2|14|1.4.5.6", Hash(executable), "../outside.dll", "00");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out diagnostic));
        Assert.Contains("escapes", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\":\"one\"}")]
    [InlineData("{\"formatVersion\":0}")]
    [InlineData("{\"formatVersion\":2}")]
    public void RejectsUnsupportedManifestFormatVersions(string manifest)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "alacrity-client-manifest.json"), manifest);

        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out string diagnostic));
        Assert.Contains("formatVersion", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DistinguishesExecutableAndRequiredRuntimeIntegrityFailures()
    {
        Directory.CreateDirectory(Path.Combine(directory, "bin"));
        string executable = Path.Combine(directory, "Alacrity.exe");
        File.WriteAllText(executable, "client");
        WriteManifest("5|2|14|1.4.5.6", "00", "bin/Alacrity.PluginUiCoreBridge.dll", "00");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out string diagnostic));
        Assert.Contains("Alacrity.exe failed", diagnostic, StringComparison.OrdinalIgnoreCase);

        WriteManifest("5|2|14|1.4.5.6", Hash(executable), "bin/Alacrity.PluginUiCoreBridge.dll", "00");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out diagnostic));
        Assert.Contains("Alacrity.PluginUiCoreBridge.dll", diagnostic, StringComparison.OrdinalIgnoreCase);

        WriteManifest("5|2|14|1.4.5.6", Hash(executable), "Alacrity.Core.dll", "00");
        Assert.False(ClientManifestIntegrity.TryValidate(directory, "5|2|14|1.4.5.6", out diagnostic));
        Assert.Contains("Alacrity.Core.dll", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private void WriteManifest(string handshake, string executableHash, string path, string hash)
    {
        File.WriteAllText(
            Path.Combine(directory, "alacrity-client-manifest.json"),
            "{\"formatVersion\":1,\"bridgeHandshake\":\"" + handshake + "\",\"outputExecutableSha256\":\"" + executableHash + "\",\"runtimeFiles\":[{\"Path\":\"" + path + "\",\"Sha256\":\"" + hash + "\"}]}");
    }

    private static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
