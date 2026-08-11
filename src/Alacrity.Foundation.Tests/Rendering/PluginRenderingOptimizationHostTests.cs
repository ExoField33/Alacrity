using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginRenderingOptimizationHostTests
{
    [Fact]
    public void ScopedPolicies_ComposeAndReleaseWithTheirActivation()
    {
        var host = new PluginRenderingOptimizationHost();
        using var first = new PluginResourceScope();
        using var second = new PluginResourceScope();

        host.CreateService(CreateManifest("tests.optimizations.first"), first).RegisterPolicy(
            new PluginRenderingOptimizationPolicy(PluginRenderingOptimization.PaintedTilePreparation));
        host.CreateService(CreateManifest("tests.optimizations.second"), second).RegisterPolicy(
            new PluginRenderingOptimizationPolicy(PluginRenderingOptimization.PaintedTilePreparation));

        Assert.Equal(PluginRenderingOptimization.PaintedTilePreparation, host.GetEffectiveOptimizations());

        first.Dispose();
        Assert.Equal(PluginRenderingOptimization.PaintedTilePreparation, host.GetEffectiveOptimizations());

        second.Dispose();
        Assert.Equal(PluginRenderingOptimization.None, host.GetEffectiveOptimizations());
    }

    [Fact]
    public void MissingRenderingCapability_IsDeniedAtTheServiceBoundary()
    {
        var host = new PluginRenderingOptimizationHost();
        using var scope = new PluginResourceScope();
        var manifest = new PluginManifest(
            new PluginId("tests.optimizations.denied"),
            "Tests",
            new Version(1, 0),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface,
            permissions: PluginPermission.DrawUserInterface,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);

        Assert.Throws<UnauthorizedAccessException>(() =>
            host.CreateService(manifest, scope).RegisterPolicy(
                new PluginRenderingOptimizationPolicy(PluginRenderingOptimization.PaintedTilePreparation)));
    }

    private static PluginManifest CreateManifest(string id)
    {
        return new PluginManifest(
            new PluginId(id),
            "Tests",
            new Version(1, 0),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Rendering,
            permissions: PluginPermission.None,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
    }
}
