using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class UpdateCounterWrapTests
{
    [Fact]
    public void OneShotUpdateDelayFiresAfterCounterWrapWithoutFiringEarly()
    {
        var scheduler = new PluginSchedulerHost();
        using var resources = new PluginResourceScope();
        var dispatcherHost = new PluginDispatcherHost();
        IPluginDispatcher dispatcher = dispatcherHost.CreateService(CreateManifest(), resources);
        IPluginScheduler service = scheduler.CreateService(CreateManifest(), resources, dispatcher, NullLogger.Instance);
        int invocations = 0;

        scheduler.Tick(uint.MaxValue - 2);
        service.AfterUpdates("wrap-once", 3, () => invocations++);

        TickAndDrain(scheduler, dispatcherHost, uint.MaxValue - 1);
        TickAndDrain(scheduler, dispatcherHost, uint.MaxValue);
        Assert.Equal(0, invocations);

        TickAndDrain(scheduler, dispatcherHost, 0);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public void RepeatingUpdateDelayContinuesAcrossCounterWrap()
    {
        var scheduler = new PluginSchedulerHost();
        using var resources = new PluginResourceScope();
        var dispatcherHost = new PluginDispatcherHost();
        IPluginDispatcher dispatcher = dispatcherHost.CreateService(CreateManifest(), resources);
        IPluginScheduler service = scheduler.CreateService(CreateManifest(), resources, dispatcher, NullLogger.Instance);
        int invocations = 0;

        scheduler.Tick(uint.MaxValue - 1);
        service.EveryUpdates("wrap-repeat", 2, () => invocations++);
        TickAndDrain(scheduler, dispatcherHost, uint.MaxValue);
        Assert.Equal(0, invocations);
        TickAndDrain(scheduler, dispatcherHost, 0);
        Assert.Equal(1, invocations);
        TickAndDrain(scheduler, dispatcherHost, 1);
        Assert.Equal(1, invocations);
        TickAndDrain(scheduler, dispatcherHost, 2);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public void UpdateIntervalsLargerThanTheUnambiguousHalfRangeAreRejected()
    {
        var scheduler = new PluginSchedulerHost();
        using var resources = new PluginResourceScope();
        var dispatcherHost = new PluginDispatcherHost();
        IPluginDispatcher dispatcher = dispatcherHost.CreateService(CreateManifest(), resources);
        IPluginScheduler service = scheduler.CreateService(CreateManifest(), resources, dispatcher, NullLogger.Instance);

        Assert.Throws<ArgumentOutOfRangeException>(delegate
        {
            service.AfterUpdates("too-far", (uint)int.MaxValue + 1U, () => { });
        });
    }

    private static void TickAndDrain(PluginSchedulerHost scheduler, PluginDispatcherHost dispatcher, uint updateVersion)
    {
        scheduler.Tick(updateVersion);
        dispatcher.Drain();
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(new PluginId("scheduler.wrap.tests"), "Scheduler wrap tests", new Version(1, 0), "Tests", "Tests update counter wrap", new[] { "1.4.5.6" });
    }

    private sealed class NullLogger : IPluginLogger
    {
        internal static readonly NullLogger Instance = new NullLogger();

        public void Debug(string message) { }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
