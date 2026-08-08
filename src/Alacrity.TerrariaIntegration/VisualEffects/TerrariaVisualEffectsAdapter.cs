using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria.VisualEffects;

/// <summary>Converts generic scoped visual-effects policies to the version-specific Terraria gates.</summary>
internal sealed class TerrariaVisualEffectsAdapter
{
    private readonly PluginVisualEffectsHost policies;
    private readonly Action<string, Exception> reportFailure;
    private TerrariaVisualEffectsRuntimePolicy current = TerrariaVisualEffectsRuntimePolicy.Vanilla;
    private PluginVisualEffectsPolicy last;

    internal TerrariaVisualEffectsAdapter(PluginVisualEffectsHost policies, Action<string, Exception> reportFailure)
    {
        this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal bool ShouldRunDustSystem => current.DustEffectsEnabled || current.HasExceptions;
    internal bool ShouldCreateDust(int dustType) => current.DustEffectsEnabled || current.ContainsDustException(dustType);
    internal bool ShouldRunGoreSystem => current.GoreEffectsEnabled;
    internal bool ShouldUpdateDustInstance(Dust dust) => dust != null && ShouldCreateDust(dust.type);

    internal void Refresh()
    {
        try
        {
            PluginVisualEffectsPolicy snapshot = policies.GetEffectivePolicy();
            if (ReferenceEquals(snapshot, last)) return;
            current = TerrariaVisualEffectsRuntimePolicy.Create(snapshot);
            last = snapshot;
        }
        catch (Exception exception)
        {
            reportFailure("Visual-effects policy", exception);
            current = TerrariaVisualEffectsRuntimePolicy.Vanilla;
            last = null;
        }
    }
}
