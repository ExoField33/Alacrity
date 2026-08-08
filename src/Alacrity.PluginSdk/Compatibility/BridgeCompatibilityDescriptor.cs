using System;
using System.Globalization;

namespace Alacrity.PluginSdk;

/// <summary>
/// Parsed compatibility information exchanged between the injected bridge and the managed host.
/// The patch-facing ABI remains a string so existing executables remain compatible; parsing lives
/// here so callers do not each reimplement fragile delimiter and integer handling.
/// </summary>
public sealed class BridgeCompatibilityDescriptor
{
    /// <summary>Creates a validated bridge compatibility descriptor.</summary>
    public BridgeCompatibilityDescriptor(int pluginSdkVersion, int hostVersion, int bridgeAbiVersion, string terrariaVersion)
    {
        if (pluginSdkVersion <= 0) throw new ArgumentOutOfRangeException(nameof(pluginSdkVersion));
        if (hostVersion <= 0) throw new ArgumentOutOfRangeException(nameof(hostVersion));
        if (bridgeAbiVersion <= 0) throw new ArgumentOutOfRangeException(nameof(bridgeAbiVersion));
        if (!TryParseTerrariaVersion(terrariaVersion, out Version parsed))
            throw new ArgumentException("Terraria compatibility must be a four-part version.", nameof(terrariaVersion));

        PluginSdkVersion = pluginSdkVersion;
        HostVersion = hostVersion;
        BridgeAbiVersion = bridgeAbiVersion;
        TerrariaVersion = parsed.ToString(4);
    }

    /// <summary>PluginSdk compatibility level.</summary>
    public int PluginSdkVersion { get; }
    /// <summary>Core host compatibility level.</summary>
    public int HostVersion { get; }
    /// <summary>Version-locked bridge ABI level.</summary>
    public int BridgeAbiVersion { get; }
    /// <summary>Exact four-part Terraria version.</summary>
    public string TerrariaVersion { get; }

    /// <summary>Formats the stable four-field handshake consumed by the injected bridge.</summary>
    public string ToHandshake() => PluginSdkVersion.ToString(CultureInfo.InvariantCulture) + "|" +
        HostVersion.ToString(CultureInfo.InvariantCulture) + "|" +
        BridgeAbiVersion.ToString(CultureInfo.InvariantCulture) + "|" + TerrariaVersion;

    /// <summary>Parses the stable bridge handshake without leaking format exceptions to startup.</summary>
    public static bool TryParse(string handshake, out BridgeCompatibilityDescriptor descriptor, out string diagnostic)
    {
        descriptor = null!;
        if (string.IsNullOrWhiteSpace(handshake))
        {
            diagnostic = "Bridge compatibility handshake is empty.";
            return false;
        }

        string[] fields = handshake!.Split('|');
        if (fields.Length != 4)
        {
            diagnostic = "Bridge compatibility handshake must contain exactly four fields (PluginSdk|Host|BridgeAbi|Terraria); found " + fields.Length.ToString(CultureInfo.InvariantCulture) + ".";
            return false;
        }

        if (!TryParsePositiveInteger(fields[0], "PluginSdk", out int pluginSdkVersion, out diagnostic) ||
            !TryParsePositiveInteger(fields[1], "Core Host", out int hostVersion, out diagnostic) ||
            !TryParsePositiveInteger(fields[2], "Bridge ABI", out int bridgeAbiVersion, out diagnostic))
            return false;
        if (!TryParseTerrariaVersion(fields[3], out Version terrariaVersion))
        {
            diagnostic = "Bridge compatibility Terraria field must be an exact four-part version; found '" + fields[3] + "'.";
            return false;
        }

        descriptor = new BridgeCompatibilityDescriptor(pluginSdkVersion, hostVersion, bridgeAbiVersion, terrariaVersion.ToString(4));
        diagnostic = string.Empty;
        return true;
    }

    /// <summary>Produces a component-by-component mismatch diagnostic against an expected host.</summary>
    public bool TryValidateAgainst(BridgeCompatibilityDescriptor expected, out string diagnostic)
    {
        if (expected == null) throw new ArgumentNullException(nameof(expected));
        if (PluginSdkVersion == expected.PluginSdkVersion && HostVersion == expected.HostVersion &&
            BridgeAbiVersion == expected.BridgeAbiVersion && string.Equals(TerrariaVersion, expected.TerrariaVersion, StringComparison.Ordinal))
        {
            diagnostic = string.Empty;
            return true;
        }

        diagnostic = "Alacrity compatibility mismatch:" + Environment.NewLine +
            "  PluginSdk: expected " + expected.PluginSdkVersion.ToString(CultureInfo.InvariantCulture) + ", found " + PluginSdkVersion.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
            "  Core Host: expected " + expected.HostVersion.ToString(CultureInfo.InvariantCulture) + ", found " + HostVersion.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
            "  Bridge ABI: expected " + expected.BridgeAbiVersion.ToString(CultureInfo.InvariantCulture) + ", found " + BridgeAbiVersion.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
            "  Terraria: expected " + expected.TerrariaVersion + ", found " + TerrariaVersion;
        return false;
    }

    private static bool TryParsePositiveInteger(string value, string name, out int result, out string diagnostic)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result <= 0)
        {
            diagnostic = "Bridge compatibility " + name + " field must be a positive invariant integer; found '" + value + "'.";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private static bool TryParseTerrariaVersion(string value, out Version version)
    {
        if (!Version.TryParse(value, out version) || version.Build < 0 || version.Revision < 0)
        {
            version = null!;
            return false;
        }

        return true;
    }
}
