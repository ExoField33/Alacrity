using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria;

/// <summary>Version-locked forwards for generic, host-owned local presentation suppression.</summary>
public static partial class PluginUiRuntime
{
    /// <summary>
    /// Returns false only when an active plugin requested removal of the Paladin shield item icon.
    /// The native boundary arcs and sparkle are intentionally outside this gate.
    /// </summary>
    public static bool ShouldDrawPaladinShieldIcon()
    {
        PluginPresentationSuppressionHost host = _presentationSuppressions;
        return host == null ||
            (host.GetEffectiveElements() & PluginPresentationElement.PaladinShieldIcon) == 0;
    }
}
