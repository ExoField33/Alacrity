using Xunit;

public sealed class ChatScenarioTests
{
    [Theory]
    [MemberData(nameof(FoundationScenarioSuite.GetScenarioCases), "Chat", MemberType = typeof(FoundationScenarioSuite))]
    public void ChatScenarioPasses(string scenario)
    {
        FoundationScenarioSuite.RunScenario(scenario);
    }
}
