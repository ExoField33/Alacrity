using Xunit;

public sealed class SettingsAndPluginScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "SettingsAndPlugins", MemberType = typeof(FoundationScenarioSuite))]
    public void SettingsAndPluginScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
