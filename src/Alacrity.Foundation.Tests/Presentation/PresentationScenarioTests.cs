using Xunit;

public sealed class PresentationScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Presentation", MemberType = typeof(FoundationScenarioSuite))]
    public void PresentationScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
