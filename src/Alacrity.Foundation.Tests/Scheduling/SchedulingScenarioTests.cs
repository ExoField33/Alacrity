using Xunit;

public sealed class SchedulingScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Scheduling", MemberType = typeof(FoundationScenarioSuite))]
    public void SchedulingScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
