using Xunit;

public sealed class CoreScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Core", MemberType = typeof(FoundationScenarioSuite))]
    public void CoreScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
