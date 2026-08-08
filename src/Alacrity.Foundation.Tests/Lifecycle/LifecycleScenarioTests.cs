using Xunit;

public sealed class LifecycleScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Lifecycle", MemberType = typeof(FoundationScenarioSuite))]
    public void LifecycleScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
