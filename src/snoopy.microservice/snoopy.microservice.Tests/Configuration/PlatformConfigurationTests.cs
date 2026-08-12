using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Platform.Generic;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

/// <summary>
/// The seam is only ever exercised through the container, so nothing else would catch a port left
/// unregistered: every consumer takes it by constructor injection and would fail on a live request.
/// </summary>
public sealed class PlatformConfigurationTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Theory]
    [InlineData("weesky", true)]
    [InlineData("generic", false)]
    public void UsesWeeskyPlatform_ReadsTheRootKey(string platform, bool expected) =>
        Assert.Equal(expected, Configuration(("Platform", platform)).UsesWeeskyPlatform());

    [Fact]
    public void UsesWeeskyPlatform_WithNoPlatform_RefusesToStartNamingTheKeyAndBothValues()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Configuration().UsesWeeskyPlatform());

        Assert.Contains("'Platform' is missing", error.Message);
        Assert.Contains("weesky", error.Message);
        Assert.Contains("generic", error.Message);
    }

    [Fact]
    public void UsesWeeskyPlatform_WithAnEmptyPlatform_RefusesToStart() =>
        Assert.Throws<InvalidOperationException>(() => Configuration(("Platform", "")).UsesWeeskyPlatform());

    [Fact]
    public void UsesWeeskyPlatform_WithAnUnknownPlatform_RefusesToStartNamingWhatItRead()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Configuration(("Platform", "gmail")).UsesWeeskyPlatform());

        Assert.Contains("Unknown Platform 'gmail'", error.Message);
        Assert.Contains("weesky", error.Message);
        Assert.Contains("generic", error.Message);
    }

    [Theory]
    [InlineData(typeof(IAliasDirectory), typeof(FreeIdentityDirectory))]
    [InlineData(typeof(IProfileReader), typeof(NullProfileReader))]
    [InlineData(typeof(IAccountInfoProvider), typeof(ClaimsAccountInfoProvider))]
    public void AddGenericPlatform_RegistersTheClaimsOnlyAdapter(Type port, Type implementation)
    {
        var services = new ServiceCollection().AddGenericPlatform();

        var descriptor = Assert.Single(services, d => d.ServiceType == port);
        Assert.Equal(implementation, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
