using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class SecurityConfigurationTests
{
    private static CorsPolicy BuildFrontendPolicy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://account.mail.weesky.net",
            })
            .Build();

        var services = new ServiceCollection().AddFrontendCors(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        return options.GetPolicy(SecurityConfiguration.CorsPolicy)!;
    }

    [Fact]
    public void AddFrontendCors_ExposesContentDispositionToJavaScript()
    {
        var policy = BuildFrontendPolicy();

        // Content-Disposition is not CORS-safelisted, so without this the browser hides it from
        // the client's fetch response and every download's filename extraction falls back silently.
        Assert.Contains("Content-Disposition", policy.ExposedHeaders);
    }

    [Fact]
    public void AddFrontendCors_AllowsTheAccountHeaderThroughPreflight()
    {
        var policy = BuildFrontendPolicy();

        // Without this entry the browser refuses every mail request carrying the account id
        // outright — the query fallback never runs because the header was already attached.
        Assert.Contains(IAccountConnectionResolver.HeaderName, policy.Headers);
    }

    private static IServiceCollection Cors(FrontendSettings? frontend, params string[] origins) =>
        new ServiceCollection().AddFrontendCors(
            new ConfigurationBuilder()
                .AddInMemoryCollection(origins.Select((origin, i) =>
                    new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", origin)))
                .Build(),
            frontend);

    [Fact]
    public void AddFrontendCors_StillRequiresAnOriginWithoutTheImagePages()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Cors(null));

        Assert.StartsWith("No CORS origin is configured.", error.Message);
    }

    [Fact]
    public void AddFrontendCors_NeedsNoOriginWhenOneAddressServesBoth()
    {
        using var provider = Cors(new FrontendSettings("/app/frontend", "", null)).BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(SecurityConfiguration.CorsPolicy)!;
        Assert.Empty(policy.Origins);
    }

    [Fact]
    public void AddFrontendCors_RequiresThePagesOriginWhenTheApiHasItsOwnAddress()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Cors(new FrontendSettings("/app/frontend", "https://api.example.net", "api.example.net")));

        Assert.Contains("Frontend:ApiBase", error.Message);
        Assert.Contains("Cors__AllowedOrigins__0", error.Message);
    }

    [Fact]
    public void AddFrontendCors_AcceptsThePagesOriginWithTwoAddresses()
    {
        using var provider = Cors(new FrontendSettings("/app/frontend", "https://api.example.net", "api.example.net"),
            "https://mail.example.net").BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(SecurityConfiguration.CorsPolicy)!;
        Assert.Equal(new[] { "https://mail.example.net" }, policy.Origins);
    }
}
