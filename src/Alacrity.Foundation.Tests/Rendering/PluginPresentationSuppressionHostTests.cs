using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginPresentationSuppressionHostTests
{
    [Fact]
    public void ScopedPolicies_ComposeAndRestoreNativePresentationOnRelease()
    {
        var host = new PluginPresentationSuppressionHost();
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();

        host.CreateService(CreateManifest("tests.presentation.first"), firstScope).RegisterPolicy(
            new PluginPresentationSuppressionPolicy(PluginPresentationElement.PaladinShieldIcon));
        host.CreateService(CreateManifest("tests.presentation.second"), secondScope).RegisterPolicy(
            new PluginPresentationSuppressionPolicy(PluginPresentationElement.PaladinShieldIcon));

        Assert.Equal(PluginPresentationElement.PaladinShieldIcon, host.GetEffectiveElements());

        firstScope.Dispose();
        Assert.Equal(PluginPresentationElement.PaladinShieldIcon, host.GetEffectiveElements());

        secondScope.Dispose();
        Assert.Equal(PluginPresentationElement.None, host.GetEffectiveElements());
    }

    [Fact]
    public void MissingRenderingCapability_IsDeniedAtTheServiceBoundary()
    {
        var host = new PluginPresentationSuppressionHost();
        using var scope = new PluginResourceScope();
        var manifest = new PluginManifest(
            new PluginId("tests.presentation.denied"),
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
                new PluginPresentationSuppressionPolicy(PluginPresentationElement.PaladinShieldIcon)));
    }

    [Fact]
    public void ReleasedService_RejectsFurtherRegistration()
    {
        var host = new PluginPresentationSuppressionHost();
        using var scope = new PluginResourceScope();
        IPluginPresentationSuppressionService service = host.CreateService(CreateManifest("tests.presentation.released"), scope);

        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            service.RegisterPolicy(new PluginPresentationSuppressionPolicy(PluginPresentationElement.PaladinShieldIcon)));
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
