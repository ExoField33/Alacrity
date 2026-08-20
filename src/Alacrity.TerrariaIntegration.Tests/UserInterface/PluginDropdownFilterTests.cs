using System.Collections.Generic;
using Alacrity.PluginSdk;
using Xunit;

namespace AlacrityTerraria.Tests.UserInterface;

public sealed class PluginDropdownFilterTests
{
    [Fact]
    public void Matches_SearchesDisplayNameAndStableValue()
    {
        Assert.True(PluginDropdownFilter.Matches("Portuguese (Brazil)", "pt-BR", "brazil"));
        Assert.True(PluginDropdownFilter.Matches("Portuguese (Brazil)", "pt-BR", "PT-br"));
        Assert.False(PluginDropdownFilter.Matches("Portuguese (Brazil)", "pt-BR", "korean"));
    }

    [Fact]
    public void Filter_ReusesDestinationAndPreservesSourceOrder()
    {
        var source = new PluginSettingOption[]
        {
            new PluginSettingOption("en", "English"),
            new PluginSettingOption("fr", "French"),
            new PluginSettingOption("fr-CA", "French (Canada)")
        };
        var destination = new List<PluginSettingOption> { new PluginSettingOption("old", "Old") };

        PluginDropdownFilter.Filter(source, "fr", destination);

        Assert.Collection(destination,
            option => Assert.Equal("fr", option.Value),
            option => Assert.Equal("fr-CA", option.Value));
    }
}
