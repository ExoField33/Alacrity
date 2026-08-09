using System;

#pragma warning disable CS1591

namespace Alacrity.PluginSdk;

/// <summary>World-render categories for which a plugin can request conservative off-screen culling.</summary>
[Flags]
public enum PluginRenderCullingCategory
{
    None = 0,
    Players = 1,
    DroppedItems = 2,
    Dust = 4,
    WorldParticles = 8
}

/// <summary>
/// Immutable local presentation policy. A request only permits the host to skip work that it has
/// verified is fully outside the current world view; unsupported renderers always remain vanilla.
/// </summary>
public sealed class PluginRenderCullingPolicy
{
    public PluginRenderCullingPolicy(PluginRenderCullingCategory categories)
    {
        Categories = categories;
    }

    public PluginRenderCullingCategory Categories { get; }
}

/// <summary>Registers activation-owned local world-render culling policy requests.</summary>
public interface IPluginRenderCullingService
{
    IPluginRegistration RegisterPolicy(PluginRenderCullingPolicy policy);
}

#pragma warning restore CS1591
