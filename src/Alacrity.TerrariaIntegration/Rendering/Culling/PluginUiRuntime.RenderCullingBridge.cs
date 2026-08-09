using Terraria;
using Terraria.Graphics.Renderers;

namespace AlacrityTerraria;

/// <summary>Version-locked forwards for generic conservative world-render culling.</summary>
public static partial class PluginUiRuntime
{
    /// <summary>Returns false only for a verified fully off-screen remote player presentation.</summary>
    public static bool ShouldDrawWorldPlayer(Player player)
    {
        return _renderCulling == null || _renderCulling.ShouldDrawPlayer(player);
    }

    /// <summary>Returns false only for a verified fully off-screen dropped-item presentation.</summary>
    public static bool ShouldDrawWorldItem(int itemIndex)
    {
        return _renderCulling == null || _renderCulling.ShouldDrawDroppedItem(itemIndex);
    }

    /// <summary>Combines visual-effect policy and conservative Dust presentation culling.</summary>
    public static bool ShouldDrawDustInstance(Dust dust)
    {
        return (_visualEffects == null || _visualEffects.ShouldUpdateDustInstance(dust)) &&
            (_renderCulling == null || _renderCulling.ShouldDrawDust(dust));
    }

    /// <summary>Fails open for particle implementations without a verified world position.</summary>
    public static bool ShouldDrawWorldParticle(ParticleRenderer renderer, IParticle particle)
    {
        return _renderCulling == null || _renderCulling.ShouldDrawWorldParticle(renderer, particle);
    }

    private static void RefreshRenderCullingPolicy()
    {
        _renderCulling?.Refresh();
    }
}
