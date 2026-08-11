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
    public void BoundsRejectMarginsOutsideThePublicBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrariaWorldSectionBounds.Calculate(
            0f,
            0f,
            800,
            600,
            1f,
            3,
            2,
            PluginWorldSectionLimits.MaximumMargin + 1,
            3200,
            2400));
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

    [Fact]
    public void IdleCapturePathSkipsDemandedCapture()
    {
        var cache = new TerrariaWorldSectionSnapshotCache();
        cache.CaptureForCurrentTick();

        for (int index = 0; index < 256; index++)
        {
            cache.CaptureForCurrentTick();
        }

        Assert.Equal(0, cache.CapturedFrameCount);
    }

    [Fact]
    public void CacheUsesIndependentBuffersAndClearsThePriorWorldWhenDemandEnds()
    {
        var cache = new TerrariaWorldSectionSnapshotCache();
        var scope = new PluginResourceScope();
        IPluginWorldSectionService service = TerrariaWorldSectionService.CreateService(cache, CreateManifest(), scope);
        var worldA = new PluginWorldSectionSnapshot(1, 2, 3200f, 4800f, 3200f, 2400f, true);
        cache.PublishSnapshotForTests(new[] { worldA }, 1, 2, 1, 2, 10, 10, 7);

        var first = new List<PluginWorldSectionSnapshot>();
        service.CopyVisibleSections(first);
        Assert.Equal(worldA.SectionX, Assert.Single(first).SectionX);
        Assert.True(cache.UsesDistinctBuffersForTests);

        scope.Dispose();

        var nextScope = new PluginResourceScope();
        IPluginWorldSectionService next = TerrariaWorldSectionService.CreateService(cache, CreateManifest(), nextScope);
        var afterWorldChange = new List<PluginWorldSectionSnapshot>();
        next.CopyVisibleSections(afterWorldChange);
        Assert.Empty(afterWorldChange);
        Assert.True(cache.UsesDistinctBuffersForTests);
        nextScope.Dispose();
    }

    [Fact]
    public void MultipleConsumersCopyTheSamePublishedDetachedFrame()
    {
        var cache = new TerrariaWorldSectionSnapshotCache();
        var firstScope = new PluginResourceScope();
        var secondScope = new PluginResourceScope();
        IPluginWorldSectionService firstService = TerrariaWorldSectionService.CreateService(cache, CreateManifest(), firstScope);
        IPluginWorldSectionService secondService = TerrariaWorldSectionService.CreateService(cache, CreateManifest(), secondScope);
        var section = new PluginWorldSectionSnapshot(3, 4, 9600f, 9600f, 3200f, 2400f, true);
        cache.PublishSnapshotForTests(new[] { section }, 3, 4, 3, 4, 10, 10, 8);

        var first = new List<PluginWorldSectionSnapshot>();
        var second = new List<PluginWorldSectionSnapshot>();
        firstService.CopyVisibleSections(first);
        secondService.CopyVisibleSections(second);

        Assert.Equal(section.SectionX, Assert.Single(first).SectionX);
        Assert.Equal(section.SectionX, Assert.Single(second).SectionX);
        Assert.Equal(2, cache.ConsumerCount);
        firstScope.Dispose();
        secondScope.Dispose();
    }

    [Fact]
    public void ServiceRejectsAnOversizedMarginBeforeRegisteringDemand()
    {
        var cache = new TerrariaWorldSectionSnapshotCache();
        var scope = new PluginResourceScope();
        IPluginWorldSectionService service = TerrariaWorldSectionService.CreateService(cache, CreateManifest(), scope);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CopyVisibleSections(
            new List<PluginWorldSectionSnapshot>(),
            PluginWorldSectionLimits.MaximumMargin + 1));
        Assert.Equal(0, cache.ConsumerCount);
        scope.Dispose();
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
