using System;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginLifecycleQuiescenceTests
{
    [Fact]
    public void SynchronousPluginCannotReactivateUntilItsPreviousBackgroundWorkQuiesces()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var plugin = new NonCooperativeBackgroundPlugin();
        using var controller = new PluginLifecycleController(
            plugin,
            host.Create(manifest),
            () => host.Create(manifest),
            TimeSpan.FromSeconds(1));

        controller.Validate();
        controller.Initialize();
        controller.Enable();
        Assert.True(plugin.WaitForWorkerStart(TimeSpan.FromSeconds(1)));

        controller.Disable();

        Assert.Equal(PluginLifecycleState.Disabling, controller.State);
        Assert.Throws<InvalidOperationException>(() => controller.Initialize());

        plugin.AllowWorkerToFinish();
        Assert.True(SpinWait.SpinUntil(() => controller.State == PluginLifecycleState.Disabled, TimeSpan.FromSeconds(1)));

        controller.Initialize();
        controller.Enable();
        Assert.Equal(PluginLifecycleState.Enabled, controller.State);
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("lifecycle.quiescence.tests"),
            "Lifecycle quiescence tests",
            new Version(1, 0),
            "Tests",
            "Exercises activation-local background quiescence.",
            new[] { "1.4.5.6" });
    }

    private sealed class NonCooperativeBackgroundPlugin : IAlacrityPlugin
    {
        private TaskCompletionSource<bool> started = NewSource();
        private TaskCompletionSource<bool> complete = NewSource();

        public void Initialize(IPluginContext context)
        {
            started = NewSource();
            complete = NewSource();
            context.Scheduler.RunBackground(
                "non-cooperative-test-worker",
                _ =>
                {
                    started.TrySetResult(true);
                    return complete.Task;
                });
        }

        public void Enable() { }

        public void Disable() { }

        public void Shutdown() { }

        internal bool WaitForWorkerStart(TimeSpan timeout)
        {
            return started.Task.Wait(timeout);
        }

        internal void AllowWorkerToFinish()
        {
            complete.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
