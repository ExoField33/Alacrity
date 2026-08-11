using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using AlacrityTerraria.GameState.Session;
using Xunit;

namespace AlacrityTerraria;

public sealed class SessionPresentationTests
{
    [Fact]
    public void ServiceAcquiresCaptureDemandOnlyAfterFirstRead()
    {
        var presentation = new TerrariaSessionPresentationService();
        using var scope = new PluginResourceScope();
        IPluginSessionPresentationService service = presentation.CreateService(CreateManifest(), scope);

        Assert.Equal(0, presentation.DemandCount);
        presentation.CaptureForCurrentTick();
        Assert.Equal(0, presentation.DemandCount);

        presentation.PublishForTests("Test server", 24, 42, 17);
        PluginSessionPresentationSnapshot snapshot = service.GetCurrent();

        Assert.Equal("Test server", snapshot.ServerName);
        Assert.Equal(24, snapshot.PlayerCapacity);
        Assert.Equal(42, snapshot.PingMilliseconds);
        Assert.Equal(1, presentation.DemandCount);
    }

    [Fact]
    public void IdleCapturePathDoesNotReadLiveTerrariaState()
    {
        var presentation = new TerrariaSessionPresentationService();
        presentation.CaptureForCurrentTick();

        for (int index = 0; index < 256; index++)
        {
            presentation.CaptureForCurrentTick();
        }

        Assert.Equal(0, presentation.DemandCount);
    }

    [Fact]
    public void ScopeReleaseDropsDemandAndPreventsStaleSessionReads()
    {
        var presentation = new TerrariaSessionPresentationService();
        var scope = new PluginResourceScope();
        IPluginSessionPresentationService service = presentation.CreateService(CreateManifest(), scope);
        presentation.PublishForTests("World A", 8, null, 3);
        service.GetCurrent();
        Assert.Equal(1, presentation.DemandCount);

        scope.Dispose();

        Assert.Equal(0, presentation.DemandCount);
        Assert.Throws<ObjectDisposedException>(() => service.GetCurrent());

        using var replacementScope = new PluginResourceScope();
        IPluginSessionPresentationService replacement = presentation.CreateService(CreateManifest(), replacementScope);
        PluginSessionPresentationSnapshot cleared = replacement.GetCurrent();
        Assert.Equal("Server", cleared.ServerName);
        Assert.Equal(0, cleared.PlayerCapacity);
        Assert.Null(cleared.PingMilliseconds);
    }

    [Fact]
    public void PublishedFrameValuesRemainCoherentForReaders()
    {
        var presentation = new TerrariaSessionPresentationService();
        using var scope = new PluginResourceScope();
        IPluginSessionPresentationService service = presentation.CreateService(CreateManifest(), scope);

        presentation.PublishForTests("A", 1, 10, 1);
        Assert.Equal("A", service.GetCurrent().ServerName);
        presentation.PublishForTests("B", 2, 20, 2);

        PluginSessionPresentationSnapshot current = service.GetCurrent();
        Assert.Equal("B", current.ServerName);
        Assert.Equal(2, current.PlayerCapacity);
        Assert.Equal(20, current.PingMilliseconds);
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("tests.session-presentation"),
            "Session Presentation Tests",
            new Version(1, 0),
            "Tests",
            "Exercises detached session presentation capture.",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.MultiplayerObservation,
            permissions: PluginPermission.ObserveMultiplayer,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
    }
}
