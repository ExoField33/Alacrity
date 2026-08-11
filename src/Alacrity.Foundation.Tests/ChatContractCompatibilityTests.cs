using System.Reflection;
using Alacrity.PluginSdk;
using Xunit;

namespace Alacrity.Foundation.Tests;

public sealed class ChatContractCompatibilityTests
{
    [Fact]
    public void ChatInputActionRetainsTheV2FourArgumentConstructor()
    {
        ConstructorInfo? constructor = typeof(ChatInputAction).GetConstructor(
            new[] { typeof(string), typeof(bool), typeof(bool), typeof(string) });

        Assert.NotNull(constructor);
        Assert.Equal(4, constructor!.GetParameters().Length);
    }

    [Fact]
    public void ChatInputEditResultRetainsTheV2FourArgumentConstructor()
    {
        ConstructorInfo? constructor = typeof(ChatInputEditResult).GetConstructor(
            new[] { typeof(string), typeof(int), typeof(int), typeof(bool) });

        Assert.NotNull(constructor);
        Assert.Equal(4, constructor!.GetParameters().Length);
    }
}
