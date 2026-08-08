using Xunit;

namespace AlacrityTerraria;

public sealed class GameStateScenarioTests
{
    [Theory]
    [MemberData(nameof(TerrariaIntegrationScenarioSuite.GetScenarioCases), "GameState", MemberType = typeof(TerrariaIntegrationScenarioSuite))]
    public void GameStateScenarioPasses(string scenario)
    {
        TerrariaIntegrationScenarioSuite.RunScenario(scenario);
    }
}
