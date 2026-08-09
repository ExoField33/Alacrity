using Xunit;

public sealed class LifecycleScenarioTests
{
    [Fact]
    public void AsyncPluginAssemblyLoaderUsesSharedRuntimeController() => FoundationScenarioSuite.AsyncPluginAssemblyLoaderUsesSharedRuntimeController();

    [Fact]
    public void CleansResourcesInReverseOrder() => FoundationScenarioSuite.LifecycleCleansResourcesInReverseOrder();

    [Fact]
    public void ReactivationCreatesFreshScopedContext() => FoundationScenarioSuite.LifecycleReactivationCreatesFreshScopedContext();

    [Fact]
    public void FailureFaultsAndCleansResources() => FoundationScenarioSuite.LifecycleFailureFaultsAndCleansResources();

    [Fact]
    public void PreservesCallbackAndCleanupFailures() => FoundationScenarioSuite.LifecyclePreservesCallbackFailureAndRecordsCleanupFailure();

    [Fact]
    public void UninstallReachesTerminalStateAfterFailures() => FoundationScenarioSuite.LifecycleUninstallReachesTerminalStateAfterFailures();

    [Fact]
    public void AsyncLifecycleSupportsCancellationAndTimeout() => FoundationScenarioSuite.AsyncLifecycleSupportsMixedActivationCancellationAndTimeout();

    [Fact]
    public void AsyncUninstallPropagatesFailures() => FoundationScenarioSuite.AsyncUninstallPropagatesLifecycleFailures();

    [Fact]
    public void AsyncLifecycleCancelsAfterCallbackStarts() => FoundationScenarioSuite.AsyncLifecycleCancelsAfterCallbackStarts();

    [Fact]
    public void AsyncShutdownIsBoundedAndRetainsFailures() => FoundationScenarioSuite.AsyncShutdownIsBoundedAndRetainsFailures();

    [Fact]
    public void ActivationTransactionRollsBackInReverseOrder() => FoundationScenarioSuite.ActivationTransactionRollsBackInReverseOrder();

    [Fact]
    public void DisableDrainsActivationBackgroundWorkBeforeReenable() => FoundationScenarioSuite.LifecycleDrainsActivationBackgroundWorkBeforeDisableAndReenable();

    [Fact]
    public void AsyncDisableDrainsActivationBackgroundWork() => FoundationScenarioSuite.AsyncLifecycleDrainsActivationBackgroundWorkBeforeDisable();

    [Fact]
    public void SynchronousDisableDoesNotWaitForNonCooperativeWork() => FoundationScenarioSuite.SynchronousDisableDoesNotWaitForNonCooperativeBackgroundWork();
}
