using Microsoft.Extensions.Configuration;
using weesky.Scotty.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Platform;

public sealed class GenericAdministratorsTests
{
    private static GenericAdministrators From(string? value) =>
        GenericAdministrators.From(new ConfigurationBuilder()
            .AddInMemoryCollection([new(GenericAdministrators.ConfigurationKey, value)])
            .Build());

    [Fact]
    public void From_ReadsEveryAddress_TrimmingSpacesAndIgnoringEmptyEntries()
    {
        var admins = From(" anne@exemple.be , michael@exemple.be ,");

        Assert.True(admins.Contains("anne@exemple.be"));
        Assert.True(admins.Contains("michael@exemple.be"));
        Assert.False(admins.Contains(""));
    }

    [Fact]
    public void Contains_IgnoresCase() =>
        Assert.True(From("Michael@Exemple.be").Contains("michael@exemple.BE"));

    [Fact]
    public void Contains_IsAnExactMatch_NotASuffixOrPrefix()
    {
        var admins = From("michael@exemple.be");

        Assert.False(admins.Contains("xmichael@exemple.be"));
        Assert.False(admins.Contains("michael@exemple.be.evil"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void From_WithNothingConfigured_HasNoAdministrator(string? value) =>
        Assert.False(From(value).Contains("michael@exemple.be"));

    [Theory]
    [InlineData("michael")]
    [InlineData("@exemple.be")]
    [InlineData("michael@")]
    [InlineData("a@b@c")]
    public void From_WithAnInvalidEntry_RefusesToStartNamingTheKeyAndTheEntry(string entry)
    {
        var error = Assert.Throws<InvalidOperationException>(() => From($"anne@exemple.be,{entry}"));

        Assert.Contains(GenericAdministrators.ConfigurationKey, error.Message);
        Assert.Contains($"'{entry}'", error.Message);
    }
}
