using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginRenderCullingHostTests
{
    [Fact]
    public void ScopedPolicies_ComposeAndRestoreVanillaOnScopeRelease()
    {
        var host = new PluginRenderCullingHost();
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();
        PluginManifest manifest = CreateManifest();

        IPluginRegistration first = host.CreateService(manifest, firstScope).RegisterPolicy(
            new PluginRenderCullingPolicy(PluginRenderCullingCategory.Players | PluginRenderCullingCategory.Dust));
        host.CreateService(manifest, secondScope).RegisterPolicy(
            new PluginRenderCullingPolicy(PluginRenderCullingCategory.DroppedItems));

        Assert.Equal(
            PluginRenderCullingCategory.Players | PluginRenderCullingCategory.DroppedItems | PluginRenderCullingCategory.Dust,
            host.GetEffectiveCategories());

        first.Dispose();
        Assert.Equal(PluginRenderCullingCategory.DroppedItems, host.GetEffectiveCategories());

        secondScope.Dispose();
        Assert.Equal(PluginRenderCullingCategory.None, host.GetEffectiveCategories());
    }

    [Fact]
    public void ReleasedScopeService_RejectsNewPolicies()
    {
        var host = new PluginRenderCullingHost();
        var scope = new PluginResourceScope();
        IPluginRenderCullingService service = host.CreateService(CreateManifest(), scope);

        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.RegisterPolicy(new PluginRenderCullingPolicy(PluginRenderCullingCategory.Players)));
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("tests.render-culling"),
            "Render Culling Tests",
            new Version(1, 0),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Rendering,
            permissions: PluginPermission.DrawUserInterface,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
    }
}
