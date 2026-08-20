using System;
using System.Diagnostics;
using Terraria;

namespace AlacrityTerraria.Rendering.Clothing;

/// <summary>
/// Render-thread coordinator for cold display-doll and hat-rack configurations. Terraria's
/// PlayerRenderer and content APIs remain on the main/render thread; this class only decides
/// when their first native invocation is admitted.
/// </summary>
internal static class TerrariaClothingEntityPreparation
{
    private const int MaximumReadyConfigurations = 2048;
    private static readonly ClothingEntityPreparationGate Gate = new ClothingEntityPreparationGate(
        CalculateBudgetTicks(),
        MaximumReadyConfigurations);

    internal static void BeginFrame()
    {
        if (Main.gameMenu)
        {
            Gate.Reset();
            return;
        }

        Gate.BeginFrame(Main.worldID, Stopwatch.GetTimestamp());
    }

    internal static bool TryAdmit(int entityKind, long visualConfiguration)
    {
        return Gate.TryAdmit(entityKind, visualConfiguration, Stopwatch.GetTimestamp());
    }

    internal static void Complete(int entityKind, long visualConfiguration)
    {
        Gate.Complete(entityKind, visualConfiguration);
    }

    internal static void Reset()
    {
        Gate.Reset();
    }

    private static long CalculateBudgetTicks()
    {
        // 1.5 ms limits a cold dense-room entry without delaying warm visual configurations.
        return Math.Max(1L, (Stopwatch.Frequency * 3L) / 2000L);
    }
}
