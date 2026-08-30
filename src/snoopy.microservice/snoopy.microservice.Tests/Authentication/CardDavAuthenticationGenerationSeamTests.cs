using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using System.Text.Encodings.Web;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using DavSecret = weesky.Snoopy.Microservice.Services.DavSecret;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

/// <summary>
/// The generation counter as the handler wires it: taken before the database read, handed to the
/// Store. Read at the cache alone, nothing proves the handler takes it at the right moment.
/// </summary>
public sealed class CardDavAuthenticationGenerationSeamTests
{
    private const string Email = "alice@weesky.be";
    private const string Secret = "ABCDEFGHIJKLMNOPQRST";
    private static readonly Guid UserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly byte[] Salt = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];

    private readonly Mock<IDavCredentialStore> credentials = new();
    private readonly Mock<IWebmailUserStore> users = new();
    private readonly Mock<IAccountInfoProvider> accounts = new();
    private readonly Mock<ILoggerFactory> loggerFactory = new();
    private readonly MutableTimeProvider clock = new();
    private readonly AuthAttemptThrottle throttle;
    private readonly DavAuthenticationCache cache;

    public CardDavAuthenticationGenerationSeamTests()
    {
        throttle = new AuthAttemptThrottle(clock);
        cache = new DavAuthenticationCache(clock);
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger<CardDavAuthenticationHandler>>());
        users.Setup(s => s.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(UserId, Guid.NewGuid()));
        accounts.Setup(s => s.IsUsableAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(true, DavSecret.Hash(Salt, Secret), Salt));
    }

    private static string Fingerprint => DavSecret.Fingerprint(Secret);

    private async Task<AuthenticateResult> AuthenticateAsync()
    {
        var jwt = new Mock<IAuthenticationService>();
        jwt.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());

        var services = new ServiceCollection();
        services.AddSingleton(jwt.Object);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Scheme = "https";
        context.Request.Headers.Authorization =
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}:{Secret}"));
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        context.Response.Body = new MemoryStream();

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);

        var handler = new CardDavAuthenticationHandler(
            new OptionsMonitorStub(), loggerFactory.Object, UrlEncoder.Default,
            credentials.Object, users.Object, accounts.Object, cache, throttle, clock, environment.Object);

        await handler.InitializeAsync(
            new AuthenticationScheme(CardDavAuthenticationDefaults.AuthenticationScheme, null,
                typeof(CardDavAuthenticationHandler)),
            context);

        return await handler.AuthenticateAsync();
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<CardDavAuthenticationOptions>
    {
        public CardDavAuthenticationOptions CurrentValue { get; } = new();
        public CardDavAuthenticationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<CardDavAuthenticationOptions, string?> listener) => null;
    }

    [Fact]
    public async Task ARevocationDuringTheRequest_IsNotWrittenBackAfterIt()
    {
        // The race itself, end to end: the rotation lands while this request is between its read
        // and its write. Without a generation taken before the read, the secret it read would be
        // republished for the whole window — the sixty seconds this task exists to close.
        users.Setup(s => s.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(UserId, Guid.NewGuid()))
            .Callback(() => cache.Forget(Email));

        var result = await AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.False(cache.TryGet(Email, Fingerprint, out _));
    }

    [Fact]
    public async Task AfterARevocation_TheNextAuthenticationCachesAgain()
    {
        // The other half, and what a handler passing a constant instead of the generation it took
        // would break: a revoked account must go back to being cached, not read from the database
        // on every single request from then on.
        cache.Forget(Email);

        var result = await AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.True(cache.TryGet(Email, Fingerprint, out var identity));
        Assert.Equal(UserId, identity.UserId);
    }
}
