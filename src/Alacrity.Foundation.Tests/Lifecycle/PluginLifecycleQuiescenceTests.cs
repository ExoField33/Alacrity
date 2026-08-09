using System;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginLifecycleQuiescenceTests
{
    [Fact]
    public void CallbackAdmissionRejectsNewEntriesAfterTeardown()
    {
        var gate = new ActivationCallbackGate();
        Assert.True(gate.TryEnter(out ActivationCallbackGate.Lease running));

        gate.CloseAdmission();

        Assert.False(gate.TryEnter(out _));
        running.Dispose();
    }

    [Fact]
    public async Task TimedOutAsyncCallbackIsQuarantinedBeforeShutdownCanRun()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var plugin = new BlockingAsyncPlugin();
        using var controller = new PluginLifecycleController(plugin, host.Create(manifest), TimeSpan.FromMilliseconds(20));

        controller.Validate();
        await controller.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<TimeoutException>(() => controller.EnableAsync(CancellationToken.None));

        await controller.DisposeAsync(CancellationToken.None);

        Assert.False(plugin.ShutdownCalled);
        plugin.CompleteEnable();
    }
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

    [Fact]
    public void RetainedSettingsControlRejectsCallbacksAfterItsActivationCloses()
    {
        using var scope = new PluginResourceScope();
        var manifest = new PluginManifest(
            new PluginId("settings.control.lifetime.tests"),
            "Settings control lifetime tests",
            new Version(1, 0),
            "Tests",
            "Exercises retained UI callback admission.",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface,
            permissions: PluginPermission.DrawUserInterface);
        var extensions = new PluginExtensionHost();
        IPluginUiService ui = extensions.CreateServices(manifest, scope).Ui;
        ui.RegisterSettingsControl(PluginSettingControl.Toggle("enabled", "Enabled", () => true, _ => { }));
        PluginSettingControl retained = extensions.GetSettingsControls(manifest.Id)[0];

        scope.CallbackGate.CloseAdmission();

        Assert.Throws<ObjectDisposedException>(() => retained.GetToggle!());
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

    private sealed class BlockingAsyncPlugin : IAsyncAlacrityPlugin
    {
        private readonly TaskCompletionSource<bool> enable = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ShutdownCalled { get; private set; }

        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken cancellationToken)
        {
            return enable.Task;
        }

        public Task DisableAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalled = true;
            return Task.CompletedTask;
        }

        internal void CompleteEnable()
        {
            enable.TrySetResult(true);
        }
    }
}
