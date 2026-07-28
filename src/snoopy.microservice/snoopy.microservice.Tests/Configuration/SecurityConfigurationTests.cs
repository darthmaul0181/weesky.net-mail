using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

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
}
