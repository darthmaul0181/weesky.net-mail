using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

/// <summary>
/// The login limiter partitions on <c>RemoteIpAddress</c>. Behind the reverse proxy that address is
/// the proxy's for every caller unless this middleware rewrites it, which collapses the partition
/// into a single global bucket — five attempts answer 429 to everybody. These tests pin the two
/// halves that make the rewrite actually happen: the header is honoured, and a proxy the
/// configuration never named cannot forge it.
/// </summary>
public sealed class ForwardedHeadersConfigurationTests
{
    private static IWebHostEnvironment Environment(string name)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }

    private static IConfiguration Configuration(params string[] knownProxies) => List("KnownProxies", knownProxies);

    private static IConfiguration Networks(params string[] knownNetworks) => List("KnownNetworks", knownNetworks);

    private static IConfiguration List(string key, string[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select((entry, i) =>
                new KeyValuePair<string, string?>($"ForwardedHeaders:{key}:{i}", entry)))
            .Build();

    private static ForwardedHeadersOptions Build(IConfiguration configuration, string environmentName)
    {
        var services = new ServiceCollection()
            .AddProxyForwardedHeaders(configuration, Environment(environmentName));
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    [Fact]
    public void AddProxyForwardedHeaders_TrustsTheConfiguredProxies()
    {
        var options = Build(Configuration("10.0.0.9", "127.0.0.1"), Environments.Production);

        Assert.Contains(IPAddress.Parse("10.0.0.9"), options.KnownProxies);
        Assert.Contains(IPAddress.Parse("127.0.0.1"), options.KnownProxies);
    }

    [Fact]
    public void AddProxyForwardedHeaders_ReadsTheClientAddressAndScheme()
    {
        var options = Build(Configuration("127.0.0.1"), Environments.Production);

        // Without XForwardedFor the partition never sees the caller. Proto travels with it so a
        // redirect or a cookie policy does not decide the request was cleartext.
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
    }

    /// <summary>
    /// One hop, always. The default is also one, but it is a security property of this deployment —
    /// a second hop would let the proxy's own client prepend an address of its choosing.
    /// </summary>
    [Fact]
    public void AddProxyForwardedHeaders_TakesOneHopOnly()
    {
        var options = Build(Configuration("127.0.0.1"), Environments.Production);

        Assert.Equal(1, options.ForwardLimit);
    }

    /// <summary>
    /// The failure this refuses is silent: an unnamed proxy makes the middleware drop the header,
    /// the address stays the proxy's, and the limiter goes on answering 429 for everyone with
    /// nothing in the log saying why. Same choice AddFrontendCors and StateDirectory.Resolve make.
    /// </summary>
    [Fact]
    public void AddProxyForwardedHeaders_RefusesToStartOutsideDevelopmentWithNoProxyNamed()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Configuration(), Environments.Production));

        Assert.Contains("ForwardedHeaders__KnownProxies__0", error.Message);
        Assert.Contains("ForwardedHeaders__KnownNetworks__0", error.Message);
    }

    [Fact]
    public void AddProxyForwardedHeaders_RunsWithoutAProxyInDevelopment()
    {
        var options = Build(Configuration(), Environments.Development);

        // Nothing sits in front of the dev server, so the connection address is already the client's.
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    /// <summary>
    /// A container's proxy gets a new address on every restart, inside its network's range: naming the
    /// range is the only way to trust it without editing the settings each time.
    /// </summary>
    [Fact]
    public void AddProxyForwardedHeaders_TrustsTheConfiguredRanges()
    {
        var options = Build(Networks("172.16.0.0/12", "fd00::/8"), Environments.Production);

        Assert.Contains(System.Net.IPNetwork.Parse("172.16.0.0/12"), options.KnownIPNetworks);
        Assert.Contains(System.Net.IPNetwork.Parse("fd00::/8"), options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Theory]
    [InlineData("172.16.0.0")]
    [InlineData("172.16.0.0/33")]
    [InlineData("not-a-range")]
    [InlineData("10.1.2.3/8")]
    public void AddProxyForwardedHeaders_RefusesToStartOnAMalformedRange(string range)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Networks(range), Environments.Production));

        Assert.Contains($"'{range}'", error.Message);
    }

    [Fact]
    public void AddProxyForwardedHeaders_NamesTheRangeAMisplacedAddressMeant()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Networks("10.1.2.3/8"), Environments.Production));

        Assert.Contains("10.0.0.0/8", error.Message);
    }

    /// <summary>
    /// A zero-length range trusts every address: any caller could then pick the address the login
    /// limiter counts, which is the limiter switched off.
    /// </summary>
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void AddProxyForwardedHeaders_RefusesARangeThatTrustsEveryone(string range)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Networks(range), Environments.Production));

        Assert.Contains($"'{range}'", error.Message);
        Assert.Contains("every address", error.Message);
    }

    [Fact]
    public void AddProxyForwardedHeaders_RefusesToStartOnAMalformedProxy()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Configuration("proxy.local"), Environments.Production));

        Assert.Contains("'proxy.local'", error.Message);
    }
}
