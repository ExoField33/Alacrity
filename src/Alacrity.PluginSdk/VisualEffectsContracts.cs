using System;
using System.Collections.Generic;

namespace Alacrity.PluginSdk;

/// <summary>Immutable visual-effect policy published by a presentation-only plugin.</summary>
public sealed class VisualEffectsPolicySnapshot
{
    /// <summary>Creates a policy snapshot. Dust exception IDs are copied by the host before use on hot paths.</summary>
    public VisualEffectsPolicySnapshot(bool dustEffectsEnabled, bool goreEffectsEnabled, IReadOnlyList<int>? dustExceptionIds)
    {
        DustEffectsEnabled = dustEffectsEnabled;
        GoreEffectsEnabled = goreEffectsEnabled;
        DustExceptionIds = dustExceptionIds ?? Array.Empty<int>();
    }

    /// <summary>Whether ordinary Dust creation, simulation, and drawing are enabled.</summary>
    public bool DustEffectsEnabled { get; }

    /// <summary>Whether Gore creation, simulation, and drawing are enabled.</summary>
    public bool GoreEffectsEnabled { get; }

    /// <summary>Dust IDs which remain enabled while ordinary Dust effects are disabled.</summary>
    public IReadOnlyList<int> DustExceptionIds { get; }
}

/// <summary>Read-only local presentation policy for host-owned Dust and Gore integration hooks.</summary>
public interface IVisualEffectsPolicyService
{
    /// <summary>Returns the current immutable policy snapshot without exposing Terraria particle objects.</summary>
    VisualEffectsPolicySnapshot GetVisualEffectsPolicy();
}
