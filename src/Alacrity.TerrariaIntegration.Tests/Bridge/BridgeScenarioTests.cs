using Xunit;

namespace AlacrityTerraria;

public sealed class BridgeScenarioTests
{
    [Fact]
    public void AbiContractRemainsStable()
    {
        TerrariaIntegrationScenarioSuite.VerifyBridgeAbiContract();
    }

    [Fact]
    public void StagedRuntimeArtifactsAreCoherent()
    {
        TerrariaIntegrationScenarioSuite.VerifyStagedRuntimeArtifacts();
    }

    [Fact]
    public void HandshakeParsingReportsCompatibilityFailures()
    {
        TerrariaIntegrationScenarioSuite.VerifyBridgeHandshakeParsing();
    }
}
