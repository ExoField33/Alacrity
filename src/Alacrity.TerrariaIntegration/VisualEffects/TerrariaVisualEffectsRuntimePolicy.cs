using System;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.VisualEffects;

/// <summary>
/// Allocation-free conversion of the generic scoped visual-effects policy into the compact checks
/// used by Terraria's version-locked dust and gore gates.
/// </summary>
internal readonly struct TerrariaVisualEffectsRuntimePolicy
{
    internal static readonly TerrariaVisualEffectsRuntimePolicy Vanilla = new TerrariaVisualEffectsRuntimePolicy(true, true, Array.Empty<bool>());
    private readonly bool[] dustExceptions;

    private TerrariaVisualEffectsRuntimePolicy(bool dustEffectsEnabled, bool goreEffectsEnabled, bool[] dustExceptions)
    {
        DustEffectsEnabled = dustEffectsEnabled;
        GoreEffectsEnabled = goreEffectsEnabled;
        this.dustExceptions = dustExceptions;
    }

    internal bool DustEffectsEnabled { get; }
    internal bool GoreEffectsEnabled { get; }
    internal bool HasExceptions => dustExceptions != null && dustExceptions.Length > 0;

    internal static TerrariaVisualEffectsRuntimePolicy Create(PluginVisualEffectsPolicy snapshot)
    {
        if (snapshot == null) return Vanilla;
        int maximum = -1;
        for (int index = 0; index < snapshot.DustExceptionIds.Count; index++)
            if (snapshot.DustExceptionIds[index] >= 0 && snapshot.DustExceptionIds[index] <= 999)
                maximum = Math.Max(maximum, snapshot.DustExceptionIds[index]);
        if (maximum < 0)
            return new TerrariaVisualEffectsRuntimePolicy(snapshot.DustEnabled, snapshot.GoreEnabled, Array.Empty<bool>());
        var exceptions = new bool[maximum + 1];
        for (int index = 0; index < snapshot.DustExceptionIds.Count; index++)
        {
            int dustType = snapshot.DustExceptionIds[index];
            if (dustType >= 0 && dustType < exceptions.Length) exceptions[dustType] = true;
        }
        return new TerrariaVisualEffectsRuntimePolicy(snapshot.DustEnabled, snapshot.GoreEnabled, exceptions);
    }

    internal bool ContainsDustException(int dustType)
    {
        return dustType >= 0 && dustExceptions != null && dustType < dustExceptions.Length && dustExceptions[dustType];
    }
}
