using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class CommandLifetimeTests
{
    [Fact]
    public void CommandFoundBeforeAdmissionClosureIsConsumedWithoutInvokingTheRetiringPlugin()
    {
        using var scope = new PluginResourceScope();
        var host = new PluginCommandHost();
        var manifest = new PluginManifest(
            new PluginId("command.lifetime.tests"),
            "Command lifetime tests",
            new Version(1, 0),
            "Tests",
            "Exercises command admission during teardown.",
            new[] { "1.4.5.6" });
        bool invoked = false;
        host.CreateService(manifest, scope, null).Register(
            new PluginCommandDescriptor("retiring", "Should not run after teardown starts"),
            _ => invoked = true);

        scope.CallbackGate.CloseAdmission();

        Assert.Equal(PluginCommandDispatchResult.Handled, host.Dispatch("retiring", Array.Empty<string>()));
        Assert.False(invoked);
    }
}
