using AlacrityTerraria.Rendering.Clothing;
using Xunit;

public sealed class ClothingEntityPreparationGateTests
{
    [Fact]
    public void CompletedConfigurationIsReusedWithoutConsumingAnotherColdAdmission()
    {
        var gate = new ClothingEntityPreparationGate(budgetTicks: 10, maximumReadyConfigurations: 8);
        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 100);

        Assert.True(gate.TryAdmit(entityKind: 1, visualConfiguration: 42, timestamp: 100));
        gate.Complete(entityKind: 1, visualConfiguration: 42);
        Assert.True(gate.TryAdmit(entityKind: 1, visualConfiguration: 42, timestamp: 500));
        Assert.Equal(1, gate.ReadyCount);
        Assert.Equal(0, gate.AdmittedCount);
    }

    [Fact]
    public void DuplicateColdConfigurationIsAdmittedOnlyOnceUntilNativeDrawCompletes()
    {
        var gate = new ClothingEntityPreparationGate(budgetTicks: 10, maximumReadyConfigurations: 8);
        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 100);

        Assert.True(gate.TryAdmit(entityKind: 1, visualConfiguration: 42, timestamp: 100));
        Assert.False(gate.TryAdmit(entityKind: 1, visualConfiguration: 42, timestamp: 100));
        Assert.Equal(1, gate.AdmittedCount);
    }

    [Fact]
    public void ChangedVisualConfigurationDoesNotReuseStalePreparedState()
    {
        var gate = new ClothingEntityPreparationGate(budgetTicks: 10, maximumReadyConfigurations: 8);
        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 100);
        Assert.True(gate.TryAdmit(entityKind: 1, visualConfiguration: 42, timestamp: 100));
        gate.Complete(entityKind: 1, visualConfiguration: 42);

        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 200);
        Assert.True(gate.TryAdmit(entityKind: 1, visualConfiguration: 43, timestamp: 200));
        Assert.False(gate.TryAdmit(entityKind: 1, visualConfiguration: 43, timestamp: 200));
    }

    [Fact]
    public void BudgetRejectsNewColdConfigurationsButNotReadyConfigurations()
    {
        var gate = new ClothingEntityPreparationGate(budgetTicks: 10, maximumReadyConfigurations: 8);
        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 100);
        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 10, timestamp: 100));
        gate.Complete(entityKind: 0, visualConfiguration: 10);

        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 10, timestamp: 111));
        Assert.False(gate.TryAdmit(entityKind: 0, visualConfiguration: 11, timestamp: 111));
    }

    [Fact]
    public void NewDrawFrameRetriesColdConfigurationsRejectedByThePriorBudget()
    {
        var gate = new ClothingEntityPreparationGate(budgetTicks: 10, maximumReadyConfigurations: 8);
        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 100);

        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 10, timestamp: 100));
        gate.Complete(entityKind: 0, visualConfiguration: 10);
        Assert.False(gate.TryAdmit(entityKind: 0, visualConfiguration: 11, timestamp: 111));

        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 200);
        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 11, timestamp: 200));
        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 10, timestamp: 200));
    }

    [Fact]
    public void WorldChangeAndResetDiscardPreparedConfigurations()
    {
        var gate = new ClothingEntityPreparationGate(budgetTicks: 10, maximumReadyConfigurations: 8);
        gate.BeginFrame(currentWorldIdentity: 1, timestamp: 100);
        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 10, timestamp: 100));
        gate.Complete(entityKind: 0, visualConfiguration: 10);

        gate.BeginFrame(currentWorldIdentity: 2, timestamp: 200);
        Assert.True(gate.TryAdmit(entityKind: 0, visualConfiguration: 10, timestamp: 200));
        gate.Reset();

        Assert.Equal(0, gate.ReadyCount);
        Assert.Equal(0, gate.AdmittedCount);
    }
}
