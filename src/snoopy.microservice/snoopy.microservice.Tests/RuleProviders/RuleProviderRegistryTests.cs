using weesky.Snoopy.Microservice.RuleProviders;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.RuleProviders;

public sealed class RuleProviderRegistryTests
{
    private static RuleProviderRegistry CreateSut() =>
        new(new IRuleProvider[] { new WeeskyRuleProvider(), new RainloopRuleProvider() });

    [Fact]
    public void Default_IsWeesky()
    {
        var sut = CreateSut();
        Assert.Equal("weesky", sut.Default.Id);
    }

    [Fact]
    public void NewAccountDefault_IsRainloop()
    {
        var sut = CreateSut();
        Assert.Equal("rainloop", sut.NewAccountDefault.Id);
    }

    [Fact]
    public void All_ListsAllProviders()
    {
        var sut = CreateSut();
        Assert.Equal(2, sut.All.Count);
    }

    [Fact]
    public void GetById_KnownId_ReturnsProvider()
    {
        var sut = CreateSut();
        Assert.Equal("rainloop", sut.GetById("rainloop")!.Id);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetById("nope"));
    }

    [Fact]
    public void GetById_Null_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.GetById(null!));
    }

    [Fact]
    public void Detect_WeeskyMarker_ReturnsWeesky()
    {
        var sut = CreateSut();
        var detected = sut.Detect("# WEESKY-RULES-V1:abc\n");
        Assert.NotNull(detected);
        Assert.Equal("weesky", detected.Id);
    }

    [Fact]
    public void Detect_RainloopMarker_ReturnsRainloop()
    {
        var sut = CreateSut();
        var detected = sut.Detect("require [\"fileinto\"];\n# RAINLOOP:SIEVE\n");
        Assert.NotNull(detected);
        Assert.Equal("rainloop", detected.Id);
    }

    [Fact]
    public void Detect_UnknownContent_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.Detect("require [\"fileinto\"];\nfileinto \"Inbox\";"));
    }

    [Fact]
    public void Constructor_NoProviders_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new RuleProviderRegistry(Array.Empty<IRuleProvider>()));
    }

    [Fact]
    public void Constructor_WithoutWeeskyProvider_DefaultFallsToFirst()
    {
        var sut = new RuleProviderRegistry(new IRuleProvider[] { new RainloopRuleProvider() });
        Assert.Equal("rainloop", sut.Default.Id);
    }

    [Fact]
    public void Constructor_WithoutRainloopProvider_NewAccountDefaultFallsToDefault()
    {
        var sut = new RuleProviderRegistry(new IRuleProvider[] { new WeeskyRuleProvider() });
        Assert.Equal("weesky", sut.NewAccountDefault.Id);
    }

    [Fact]
    public void Detect_EmptyString_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(sut.Detect(string.Empty));
    }
}
