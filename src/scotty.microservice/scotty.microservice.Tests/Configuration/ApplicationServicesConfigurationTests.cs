using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

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
        using var provider = BuildOptions(new Dictionary<string, string?>(Servers) { [$"Mail:{key}"] = value });

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<MailOptions>>().Value);

        Assert.Contains("Mail:", error.Message);
    }

    /// <summary>ContactStore's projector is an optional parameter: only the container filling it arms
    /// the guard that stops a contact write from leaving a stale birthday behind.</summary>
    [Fact]
    public async Task TheContainer_HandsContactStoreItsBirthdayProjector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(Guid.NewGuid().ToString()));
        services.AddRepositories();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<ContactStore>();

        Assert.True(scope.ServiceProvider.GetRequiredService<PreferencesDbContext>().BirthdayProjectionRequired);
    }

    [Fact]
    public void MailOptions_WithTheShippedValues_Validate()
    {
        using var provider = BuildOptions(new Dictionary<string, string?>(Servers) { ["Mail:TimeoutSeconds"] = "30" });

        Assert.Equal(30, provider.GetRequiredService<IOptions<MailOptions>>().Value.TimeoutSeconds);
    }

    /// <summary>
    /// Without its servers the webmail cannot sign anyone in; worse, a value inherited from a
    /// shipped default would send every password to someone else's server. Refused at start.
    /// </summary>
    [Theory]
    [InlineData("ImapHost")]
    [InlineData("SmtpHost")]
    public void MailOptions_WithoutAServer_AreRefused(string key)
    {
        using var provider = BuildOptions(new Dictionary<string, string?>(Servers) { [$"Mail:{key}"] = " " });

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<MailOptions>>().Value);

        Assert.Contains($"Mail__{key}", error.Message);
    }

    private static readonly Dictionary<string, string?> Servers = new()
    {
        ["Mail:ImapHost"] = "imap.example.test",
        ["Mail:SmtpHost"] = "smtp.example.test",
    };

    private static ServiceProvider BuildOptions(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddScottyOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }
}
