using System;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria;

/// <summary>Version-locked forwarding for the generic lighting-parallelism policy.</summary>
public static partial class PluginUiRuntime
{
    /// <summary>
    /// Executes a verified LightMap or TileLightScanner range. If no activation requested the
    /// optimization, the exact native FastParallel implementation remains authoritative.
    /// </summary>
    public static bool TryRunLightingParallel(
        int fromInclusive,
        int toExclusive,
        Delegate callback,
        object context)
    {
        PluginRenderingOptimizationHost host = _renderingOptimizations;
        if (host != null &&
            (host.GetEffectiveOptimizations() & PluginRenderingOptimization.LightingParallelism) != 0)
        {
            Rendering.Optimization.TerrariaLightingParallelExecutor.For(
                fromInclusive,
                toExclusive,
                callback,
                context);
            return true;
        }

        return false;
    }
}
