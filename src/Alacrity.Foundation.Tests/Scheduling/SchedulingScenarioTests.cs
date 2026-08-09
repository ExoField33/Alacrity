using Xunit;

public sealed class SchedulingScenarioTests
{
    [Fact]
    public void DispatcherHonorsFrameBudget() => FoundationScenarioSuite.DispatcherHonorsFrameBudget();

    [Fact]
    public void DispatcherRetainsPhysicalQueueSlotsAfterCancellation() => FoundationScenarioSuite.DispatcherRetainsPhysicalQueueSlotsAfterCancellation();

    [Fact]
    public void SchedulerUsesDispatcherAndActivationCleanup() => FoundationScenarioSuite.SchedulerUsesDispatcherAndActivationCleanup();

    [Fact]
    public void EmptyDispatcherDrainIsAllocationFree() => FoundationScenarioSuite.EmptyDispatcherDrainIsAllocationFree();

    [Fact]
    public void SchedulerTickWithoutDueWorkIsAllocationFree() => FoundationScenarioSuite.SchedulerTickWithoutDueWorkIsAllocationFree();

    [Fact]
    public void SchedulerElapsedWorkUsesMonotonicClockUnits() => FoundationScenarioSuite.SchedulerElapsedWorkUsesMonotonicClockUnits();

    [Fact]
    public void BackgroundWorkIsBoundedAndActivationOwned() => FoundationScenarioSuite.BackgroundWorkIsBoundedAndActivationOwned();

    [Fact]
    public void TransientResourcesAreReleased() => FoundationScenarioSuite.TransientSchedulerAndDispatcherResourcesAreReleased();
}
