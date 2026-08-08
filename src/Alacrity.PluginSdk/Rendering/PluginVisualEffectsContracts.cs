using System;
using System.Collections.Generic;

#pragma warning disable CS1591

namespace Alacrity.PluginSdk;

/// <summary>Immutable presentation-only visual-effects policy registered through the Terraria host service.</summary>
public sealed class PluginVisualEffectsPolicy
{
    public PluginVisualEffectsPolicy(bool dustEnabled, bool goreEnabled, IReadOnlyList<int>? dustExceptionIds = null)
    {
        DustEnabled = dustEnabled;
        GoreEnabled = goreEnabled;
        DustExceptionIds = dustExceptionIds ?? Array.Empty<int>();
    }
    public bool DustEnabled { get; }
    public bool GoreEnabled { get; }
    public IReadOnlyList<int> DustExceptionIds { get; }
}

/// <summary>Registers scope-owned presentation policies; multiple policies compose conservatively.</summary>
public interface IPluginVisualEffectsService
{
    IPluginRegistration RegisterPolicy(PluginVisualEffectsPolicy policy);
}

#pragma warning restore CS1591
