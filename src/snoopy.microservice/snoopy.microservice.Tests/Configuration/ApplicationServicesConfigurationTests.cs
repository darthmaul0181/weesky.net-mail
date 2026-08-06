using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

public sealed class ApplicationServicesConfigurationTests
{
    /// <summary>
    /// A 307/308 from the token endpoint would re-POST the client secret and the refresh token to
    /// whatever host it names: the token client's primary handler must never follow one.
    /// </summary>
    [Fact]
    public void TheTokenClient_NeverFollowsARedirect()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMailServices();
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(IOAuthTokenService));
        while (handler is DelegatingHandler delegating) handler = delegating.InnerHandler!;

        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
    }
}
