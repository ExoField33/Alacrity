using System.Collections.Generic;
using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using AlacrityTerraria.GameState.World;
using Xunit;

namespace AlacrityTerraria;

public sealed class WorldSectionSnapshotTests
{
    [Fact]
    public void BoundsPreserveMarginAndClampToWorldEdges()
    {
        TerrariaWorldSectionBounds bounds = TerrariaWorldSectionBounds.Calculate(
            100f,
            100f,
            800,
            600,
            1f,
            3,
            2,
            2,
            3200,
            2400);

        Assert.Equal(0, bounds.StartX);
        Assert.Equal(0, bounds.StartY);
        Assert.Equal(2, bounds.EndX);
        Assert.Equal(2, bounds.EndY);
    }

    [Fact]
    public void ServiceAcquiresDemandLazilyAndRejectsUseAfterScopeRelease()
    {
        var cache = new TerrariaWorldSectionSnapshotCache();
        var scope = new PluginResourceScope();
        IPluginWorldSectionService service = TerrariaWorldSectionService.CreateService(cache, CreateManifest(), scope);
        Assert.Equal(0, cache.ConsumerCount);

        service.CopyVisibleSections(new List<PluginWorldSectionSnapshot>(), 1);
        Assert.Equal(1, cache.ConsumerCount);

        scope.Dispose();
        Assert.Equal(0, cache.ConsumerCount);
        Assert.Throws<ObjectDisposedException>(() => service.CopyVisibleSections(new List<PluginWorldSectionSnapshot>()));
    }

    [Fact]
    public void IdleCacheDoesNotTouchTerrariaCaptureState()
    {
        var cache = new TerrariaWorldSectionSnapshotCache();

        cache.CaptureForCurrentTick();

        Assert.Equal(0, cache.ConsumerCount);
        Assert.Equal(0, cache.CapturedFrameCount);
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("tests.world-sections"),
            "World Sections Tests",
            new Version(1, 0),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.GameStateRead,
            permissions: PluginPermission.ReadGameState,
            multiplayerSafety: MultiplayerSafety.ClientPresentationOnly);
    }
}
