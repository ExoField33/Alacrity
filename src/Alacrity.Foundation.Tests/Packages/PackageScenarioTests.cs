using Xunit;

public sealed class PackageScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Packages", MemberType = typeof(FoundationScenarioSuite))]
    public void PackageScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
