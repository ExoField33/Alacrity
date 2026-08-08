using Xunit;

public sealed class PatchingScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Patching", MemberType = typeof(FoundationScenarioSuite))]
    public void PatchingScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
