using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models.Mail;
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
    public async Task TheTokenClient_NeverFollowsARedirect()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMailServices();
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(IOAuthTokenService));
        while (handler is DelegatingHandler delegating) handler = delegating.InnerHandler!;

        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
    }

    /// <summary>
    /// A zero or negative budget turns into <c>CancelAfter(negative)</c>: a 500 on the borrow path
    /// with the entry stuck out, and a client the background close never disposes. Refused at
    /// startup, where an operator is watching, rather than on the first request that meets it.
    /// </summary>
    [Theory]
    [InlineData("TimeoutSeconds", "0")]
    [InlineData("PoolHealthTimeoutSeconds", "0")]
    [InlineData("PoolMaxLifetimeMinutes", "0")]
    [InlineData("PoolIdleSeconds", "-1")]
    [InlineData("PoolMaxPerIdentity", "-1")]
    [InlineData("PoolMaxTotal", "-1")]
    public void MailOptions_WithANonPositiveBudget_AreRefused(string key, string value)
    {
        using var provider = BuildOptions(new Dictionary<string, string?> { [$"Mail:{key}"] = value });

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<MailOptions>>().Value);

        Assert.Contains("Mail:", error.Message);
    }

    [Fact]
    public void MailOptions_WithTheShippedValues_Validate()
    {
        using var provider = BuildOptions(new Dictionary<string, string?> { ["Mail:TimeoutSeconds"] = "30" });

        Assert.Equal(30, provider.GetRequiredService<IOptions<MailOptions>>().Value.TimeoutSeconds);
    }

    private static ServiceProvider BuildOptions(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSnoopyOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }
}
