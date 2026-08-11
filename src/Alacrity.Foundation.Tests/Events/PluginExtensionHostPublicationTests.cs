using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginExtensionHostPublicationTests
{
    [Fact]
    public void PublishWithNoSubscribersDoesNotAllocate()
    {
        var host = new PluginExtensionHost();
        host.Publish(new ClientUpdatedEvent(1, TimeSpan.Zero));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
        {
            host.Publish(new ClientUpdatedEvent((uint)index, TimeSpan.Zero));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void StableValueTypeSubscribersPublishWithoutSnapshotOrBoxingAllocations()
    {
        var host = new PluginExtensionHost();
        using var scope = new PluginResourceScope();
        PluginExtensionHost.PluginExtensionServices services = host.CreateServices(CreateManifest(), scope);
        var received = 0;
        services.Events.Subscribe<ClientUpdatedEvent>(_ => received++);
        // Let generic dispatch and tiered JIT settle before measuring only the steady-state path.
        for (int index = 0; index < 4096; index++)
        {
            host.Publish(new ClientUpdatedEvent((uint)index, TimeSpan.Zero));
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
        {
            host.Publish(new ClientUpdatedEvent((uint)index, TimeSpan.Zero));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(4352, received);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RegistrationChangesPublishTheNewImmutableSnapshotAndOnceHandlersRemoveThemselves()
    {
        var host = new PluginExtensionHost();
        using var firstScope = new PluginResourceScope();
        using var secondScope = new PluginResourceScope();
        PluginExtensionHost.PluginExtensionServices first = host.CreateServices(CreateManifest("tests.events.first"), firstScope);
        PluginExtensionHost.PluginExtensionServices second = host.CreateServices(CreateManifest("tests.events.second"), secondScope);
        var firstCalls = 0;
        var secondCalls = 0;
        first.Events.Subscribe<string>(_ => firstCalls++);
        second.Events.Subscribe<string>(_ => secondCalls++, new PluginEventOptions { Once = true });

        host.Publish("first");
        host.Publish("second");
        firstScope.Dispose();
        host.Publish("third");

        Assert.Equal(2, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void DisposalDuringPublicationPreventsTheRemovedSnapshotEntryFromRunning()
    {
        var host = new PluginExtensionHost();
        using var scope = new PluginResourceScope();
        PluginExtensionHost.PluginExtensionServices services = host.CreateServices(CreateManifest(), scope);
        IPluginRegistration? second = null;
        var firstCalls = 0;
        var secondCalls = 0;
        services.Events.Subscribe<string>(_ =>
        {
            firstCalls++;
            second!.Dispose();
        });
        second = services.Events.Subscribe<string>(_ => secondCalls++);

        host.Publish("dispose sibling");

        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);
    }

    private static PluginManifest CreateManifest(string id = "tests.events")
    {
        return new PluginManifest(
            new PluginId(id),
            "Event Tests",
            new Version(1, 0),
            "Tests",
            "Exercises allocation-free event delivery.",
            new[] { "1.4.5.6" });
    }
}
