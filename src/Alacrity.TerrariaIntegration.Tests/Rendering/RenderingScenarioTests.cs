using Xunit;

namespace AlacrityTerraria;

public sealed class RenderingScenarioTests
{
    [Theory]
    [MemberData(nameof(TerrariaIntegrationScenarioSuite.GetScenarioCases), "Rendering", MemberType = typeof(TerrariaIntegrationScenarioSuite))]
    public void RenderingScenarioPasses(string scenario)
    {
        TerrariaIntegrationScenarioSuite.RunScenario(scenario);
    }
}
