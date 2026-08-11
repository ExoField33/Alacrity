using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria;

/// <summary>Version-locked forwards for generic, host-owned renderer preparation optimizations.</summary>
public static partial class PluginUiRuntime
{
    /// <summary>
    /// Returns whether a scoped plugin requested the verified painted-tile preparation optimization.
    /// A missing or unavailable runtime always returns false so patched Terraria retains vanilla work.
    /// </summary>
    public static bool IsPaintPreparationOptimizationEnabled()
    {
        PluginRenderingOptimizationHost host = _renderingOptimizations;
        return host != null &&
            (host.GetEffectiveOptimizations() & PluginRenderingOptimization.PaintedTilePreparation) != 0;
    }

    /// <summary>
    /// Filters the expensive foliage preparation switch only while the optimization is active.
    /// The result is deliberately true when unavailable so a stale optional bridge fails open.
    /// </summary>
    public static bool IsPaintExtraPreparationRelevant(int tileType)
    {
        if (!IsPaintPreparationOptimizationEnabled())
        {
            return true;
        }

        return tileType == 5 ||
            tileType == 323 ||
            (tileType >= 583 && tileType <= 589) ||
            tileType == 596 ||
            tileType == 616 ||
            tileType == 634;
    }

    /// <summary>
    /// Returns whether a scoped plugin requested the verified local clothing-entity presentation
    /// optimization. Missing bridge state returns false so Terraria executes its original passes.
    /// </summary>
    public static bool IsClothingEntityPresentationOptimizationEnabled()
    {
        PluginRenderingOptimizationHost host = _renderingOptimizations;
        return host != null &&
            (host.GetEffectiveOptimizations() & PluginRenderingOptimization.ClothingEntityPresentation) != 0;
    }

    /// <summary>
    /// Returns whether a scoped plugin requested the verified local waterfall presentation
    /// optimization. A missing bridge leaves Terraria on its unmodified rendering path.
    /// </summary>
    public static bool IsWaterfallPresentationOptimizationEnabled()
    {
        PluginRenderingOptimizationHost host = _renderingOptimizations;
        return host != null &&
            (host.GetEffectiveOptimizations() & PluginRenderingOptimization.WaterfallPresentation) != 0;
    }

    /// <summary>
    /// Returns whether a scoped plugin requested verified reductions in Terraria's common tile
    /// renderer. Missing bridge state keeps every native tile path enabled.
    /// </summary>
    public static bool IsTileDrawingPresentationOptimizationEnabled()
    {
        PluginRenderingOptimizationHost host = _renderingOptimizations;
        return host != null &&
            (host.GetEffectiveOptimizations() & PluginRenderingOptimization.TileDrawingPresentation) != 0;
    }

    /// <summary>
    /// Returns whether a plugin requested the verified top-level draw-orchestration reductions.
    /// Missing bridge state retains the untouched Terraria path.
    /// </summary>
    public static bool IsDrawOrchestrationOptimizationEnabled()
    {
        PluginRenderingOptimizationHost host = _renderingOptimizations;
        return host != null &&
            (host.GetEffectiveOptimizations() & PluginRenderingOptimization.DrawOrchestration) != 0;
    }
}
