using Xunit;

public sealed class TileStorageScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "TileStorage", MemberType = typeof(FoundationScenarioSuite))]
    public void TileStorageScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
