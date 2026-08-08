using Xunit;

namespace AlacrityTerraria;

public sealed class BridgeScenarioTests
{
    [Theory]
    [MemberData(nameof(TerrariaIntegrationScenarioSuite.GetScenarioCases), "Bridge", MemberType = typeof(TerrariaIntegrationScenarioSuite))]
    public void BridgeScenarioPasses(string scenario)
    {
        TerrariaIntegrationScenarioSuite.RunScenario(scenario);
    }
}
