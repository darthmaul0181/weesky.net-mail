using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using DavSecret = weesky.Snoopy.Microservice.Services.DavSecret;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class CardDavAuthenticationHandlerTests
{
    private const string Email = "alice@weesky.be";
    private const string Secret = "ABCDEFGHIJKLMNOPQRST";
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly byte[] Salt = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];

    private readonly Mock<IDavCredentialStore> credentials = new();
    private readonly Mock<IWebmailUserStore> users = new();
    private readonly Mock<IAccountInfoProvider> accounts = new();
    private readonly Mock<IAuthenticationService> jwt = new();
    private readonly Mock<ILogger<CardDavAuthenticationHandler>> log = new();
    private readonly Mock<ILoggerFactory> loggerFactory = new();
    private readonly MutableTimeProvider clock = new();
    private readonly AuthAttemptThrottle throttle;
    private readonly DavAuthenticationCache cache;

    public CardDavAuthenticationHandlerTests()
    {
        throttle = new AuthAttemptThrottle(clock);
        cache = new DavAuthenticationCache(clock);
        // The handler logs through the one ILogger its base class builds, so the test's logger has
        // to arrive through the factory rather than as a second injected logger for the same type.
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(log.Object);
        users.Setup(s => s.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(UserId, Guid.NewGuid()));
        accounts.Setup(s => s.IsUsableAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(true, DavSecretHash(Secret), Salt));
        jwt.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
    }

    private static string DavSecretHash(string secret) => DavSecret.Hash(Salt, secret);

    private static string Basic(string user, string secret) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}"));

    private async Task<(AuthenticateResult Result, DefaultHttpContext Context)> AuthenticateAsync(
        string? authorization, string scheme = "https", string? environment = null,
        string? remoteIp = "203.0.113.7", TimeProvider? handlerClock = null,
        CancellationToken aborted = default)
    {
        var services = new ServiceCollection();
        services.AddSingleton(jwt.Object);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Scheme = scheme;
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        if (remoteIp is not null) context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        context.Response.Body = new MemoryStream();
        context.RequestAborted = aborted;

        var env = new Mock<IHostEnvironment>();
        // Environments.Production is static readonly, not const, so it cannot be a default value.
        env.SetupGet(e => e.EnvironmentName).Returns(environment ?? Environments.Production);

        var handler = new CardDavAuthenticationHandler(
            new OptionsMonitorStub(), loggerFactory.Object, UrlEncoder.Default,
            credentials.Object, users.Object, accounts.Object, cache, throttle,
            handlerClock ?? clock, env.Object);

        await handler.InitializeAsync(
            new AuthenticationScheme(CardDavAuthenticationDefaults.AuthenticationScheme, null,
                typeof(CardDavAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();
        if (!result.Succeeded) await handler.ChallengeAsync(null);

        return (result, context);
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<CardDavAuthenticationOptions>
    {
        public CardDavAuthenticationOptions CurrentValue { get; } = new();
        public CardDavAuthenticationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<CardDavAuthenticationOptions, string?> listener) => null;
    }

    [Fact]
    public async Task AValidSecret_AuthenticatesWithTheSameClaimsAsTheJwt()
    {
        var (result, _) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.True(result.Succeeded);
        var claims = result.Principal!.Claims.ToList();
        Assert.Equal("alice", claims.Single(c => c.Type == ClaimTypes.Upn).Value);
        Assert.Equal("weesky.be", claims.Single(c => c.Type == ClaimTypes.Dns).Value);
        Assert.Equal(UserId.ToString(), claims.Single(c => c.Type == WebmailClaimTypes.Uid).Value);
        // Never a session stamp: the secret is not a session and carries none (décision 2).
        Assert.DoesNotContain(claims, c => c.Type == WebmailClaimTypes.Stamp);
    }

    [Fact]
    public async Task ASecretWithEdgeWhitespace_IsAccepted()
    {
        var (result, _) = await AuthenticateAsync(Basic(Email, $" {Secret} "));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AWrongSecret_Is401WithABasicChallengeAndNoBearerOne()
    {
        var (result, context) = await AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var challenge = Assert.Single(context.Response.Headers.WWWAuthenticate!);
        Assert.Equal($"Basic realm=\"{CardDavAuthenticationDefaults.Realm}\"", challenge);
        // The realm is a keychain key on the client: it must never vary between deployments.
        Assert.DoesNotContain("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownAccount_Is401()
    {
        users.Setup(s => s.FindByEmailAsync("ghost@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebmailAccount?)null);

        var (result, context) = await AuthenticateAsync(Basic("ghost@weesky.be", Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnotherAccountsSecret_Is401()
    {
        // Per-row salt, so the same string presented under another identifier never matches: the
        // digest of user B is not the digest of user A even for one and the same secret.
        var otherSalt = new byte[16];
        Array.Fill(otherSalt, (byte)9);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(true, DavSecret.Hash(otherSalt, Secret), Salt));

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AReplacedSecret_Is401()
    {
        // What a regeneration must produce at the edge, and the reason Forget exists: the previous
        // secret stops working rather than living out the cache window on this instance.
        var (first, _) = await AuthenticateAsync(Basic(Email, Secret));
        Assert.True(first.Succeeded);
        cache.Forget(Email);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(
                true, DavSecret.Hash(Salt, "TSRQPONMLKJIHGFEDCBA"), Salt));

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnAccountTheMailServerNoLongerHolds_Is401()
    {
        // The address book must not be the last open door of a closed account.
        accounts.Setup(s => s.IsUsableAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnAccountThatNeverEnabled_Is401()
    {
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DavCredentialRecord?)null);

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AMalformedStoredDigest_Is401AndIsLoggedOnTheGuidAlone()
    {
        // Indistinguishable from a wrong secret at DavSecret.Matches, which cannot log by
        // constraint. The answer stays 401; the log line is what tells an operator it is a
        // storage fault, and it names the GUID and nothing else.
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(true, "deadbeef", Salt));

        var (_, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        VerifyLogged(LogLevel.Error, line => line.Contains(UserId.ToString()));
        VerifyNeverLogged(line => line.Contains(Secret) || line.Contains(Email));
    }

    [Fact]
    public async Task SwitchedOff_IsForbiddenOnAGoodSecret_AndUnauthorizedOnABadOne()
    {
        // The pair, because it is the pair that attests the order of décision 2 and closes the
        // account-enumeration oracle: 403 is only ever visible to whoever already holds the secret.
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(false, DavSecretHash(Secret), Salt));

        var (_, good) = await AuthenticateAsync(Basic(Email, Secret));
        Assert.Equal(StatusCodes.Status403Forbidden, good.Response.StatusCode);
        Assert.Equal(0, good.Response.Headers.WWWAuthenticate.Count);

        var (_, bad) = await AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"));
        Assert.Equal(StatusCodes.Status401Unauthorized, bad.Response.StatusCode);
    }

    [Fact]
    public async Task PlainHttp_Is403_ReadsNothingAndCostsNothing()
    {
        // One short of the threshold, so a throttle write by the handler would tip it over.
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure(Email, "203.0.113.7");

        var (_, context) = await AuthenticateAsync(Basic(Email, Secret), scheme: "http");

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        // Asserted on the store, not on the status: nothing is ever compared to a secret its own
        // transport already gave away.
        credentials.Verify(s => s.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(throttle.IsBlocked(Email, "203.0.113.7", out _));
        AssertNoDelayWasRequested();
    }

    [Fact]
    public async Task PlainHttpInDevelopment_IsAllowed()
    {
        var (result, _) = await AuthenticateAsync(
            Basic(Email, Secret), scheme: "http", environment: Environments.Development);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PastTheThreshold_Is429_ReadsNothingAndCostsNothing()
    {
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure(Email, "203.0.113.7");
        credentials.Invocations.Clear();

        var (_, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        var retryAfter = Assert.Single(context.Response.Headers.RetryAfter!);
        Assert.True(int.Parse(retryAfter!, CultureInfo.InvariantCulture) > 0);
        // Never 401: during an attack on one identifier, every device of the victim would be told
        // its secret went bad.
        Assert.Equal(0, context.Response.Headers.WWWAuthenticate.Count);
        credentials.Verify(s => s.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        // A request past the threshold must cost nothing at all, the slowdown included.
        AssertNoDelayWasRequested();
    }

    [Fact]
    public async Task ASuccessClearsTheIdentifiersFailures()
    {
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure(Email, "203.0.113.7");

        await AuthenticateAsync(Basic(Email, Secret));
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure(Email, "198.51.100.4");

        Assert.False(throttle.IsBlocked(Email, "198.51.100.4", out _));
    }

    [Fact]
    public async Task AFailureIsDelayedWithinTheSpecifiedWindow()
    {
        // Observed rather than chronometered: the wait is taken on the injected clock, so the
        // refusal is provably still pending while the clock stands still, and the amount asked for
        // is read back instead of inferred from a wall clock a loaded runner also writes to.
        clock.HoldTimers = true;

        var pending = AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"));
        await clock.WaitForPendingTimerAsync();

        Assert.False(pending.IsCompleted);
        var asked = Assert.Single(clock.RequestedDelays);
        Assert.InRange(asked, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500));

        clock.Now = clock.Now.Add(TimeSpan.FromMilliseconds(1500));
        var (result, context) = await pending;

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AClientHangingUpDuringTheDelay_IsARefusalAndNotAnEscapingException()
    {
        // The base AuthenticateAsync runs HandleAuthenticateOnceAsync, which does not swallow: an
        // uncaught cancellation would leave the pipeline instead of the 401 this path was heading
        // for. The attempt still counts, since the throttle is written before the wait begins.
        clock.HoldTimers = true;
        using var aborted = new CancellationTokenSource();

        var pending = AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"), aborted: aborted.Token);
        await clock.WaitForPendingTimerAsync();
        await aborted.CancelAsync();

        var (result, context) = await pending;

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(2, throttle.TrackedKeys);
    }

    [Fact]
    public async Task ARequestWithoutARemoteAddress_IsRefusedAndCountsOnTheIdentifierAlone()
    {
        // Kestrel reports no peer for some connections, and the throttle then drops the address
        // key entirely rather than keying every such caller onto one shared empty string.
        var (result, context) = await AuthenticateAsync(
            Basic(Email, "WRONGWRONGWRONGWRONG"), remoteIp: null);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(1, throttle.TrackedKeys);
    }

    [Fact]
    public async Task ConcurrentFailuresWaitTogether_SoTheDelayNeverBlocksAThread()
    {
        // Twenty failures started one after another from this thread. Awaited, the waits overlap
        // and the batch costs one window; slept, each would block the starter before the next one
        // began and the batch would cost twenty.
        const int Callers = 20;
        var elapsed = Stopwatch.StartNew();

        // The real clock on purpose: this is the one test whose subject is real concurrency, so a
        // wait the test itself completes would prove nothing about threads.
        await Task.WhenAll(Enumerable.Range(0, Callers)
            .Select(i => AuthenticateAsync(
                Basic($"ghost{i}@weesky.be", Secret), remoteIp: $"203.0.113.{i}",
                handlerClock: TimeProvider.System))
            .ToArray());

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5),
            $"{Callers} concurrent failures took {elapsed.Elapsed}; serialised they would take ~{Callers}s.");
    }

    [Fact]
    public async Task NoAuthorizationHeader_DelegatesToTheJwtAndStillChallengesBasic()
    {
        var (result, context) = await AuthenticateAsync(authorization: null);

        jwt.Verify(s => s.AuthenticateAsync(It.IsAny<HttpContext>(),
            JwtBearerDefaults.AuthenticationScheme), Times.Once);
        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("Basic", Assert.Single(context.Response.Headers.WWWAuthenticate!)!);
    }

    [Fact]
    public async Task AValidJwt_IsAcceptedOnThisSchemeToo()
    {
        // What keeps the whole /dav surface testable from an ordinary webmail session, with no
        // secret generated at all.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Upn, "alice"), new Claim(ClaimTypes.Dns, "weesky.be")], "Bearer"));
        jwt.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "Bearer")));

        var (result, _) = await AuthenticateAsync(authorization: null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AValidJwtInABearerHeader_DelegatesAndIsAccepted()
    {
        // Only the browser sends its token in a cookie. Swagger's Authorize button, curl and every
        // other API consumer send this header, and refusing it would remove the JWT fallback that
        // delegation exists for.
        jwt.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Upn, "alice"), new Claim(ClaimTypes.Dns, "weesky.be")], "Bearer")),
                "Bearer")));

        var (result, context) = await AuthenticateAsync("Bearer eyJhbGciOiJIUzI1NiJ9.e30.s");

        Assert.True(result.Succeeded);
        Assert.Equal(0, context.Response.Headers.WWWAuthenticate.Count);
        jwt.Verify(s => s.AuthenticateAsync(It.IsAny<HttpContext>(),
            JwtBearerDefaults.AuthenticationScheme), Times.Once);
    }

    [Fact]
    public async Task AMalformedBasicHeader_Is401WithoutReadingTheTable()
    {
        var (_, context) = await AuthenticateAsync("Basic not-base64!!");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        credentials.Verify(s => s.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABurstReadsTheTableOnce()
    {
        await AuthenticateAsync(Basic(Email, Secret));
        await AuthenticateAsync(Basic(Email, Secret));
        await AuthenticateAsync(Basic(Email, Secret));

        credentials.Verify(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AMixedCaseIdentifier_IsCanonicalisedBeforeTheCache()
    {
        // The cache compares byte for byte and never compensates, so the caller canonicalises —
        // the same Trim/lower WebmailUserStore applies. Without it one account holds one entry per
        // spelling and Forget only ever revokes the spelling it was handed.
        var (first, _) = await AuthenticateAsync(Basic(" Alice@Weesky.BE ", Secret));
        var (second, _) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal("alice", first.Principal!.Claims.Single(c => c.Type == ClaimTypes.Upn).Value);
        credentials.Verify(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LastUsedIsWrittenOncePerHour()
    {
        await AuthenticateAsync(Basic(Email, Secret));
        clock.Now = clock.Now.AddMinutes(2);
        await AuthenticateAsync(Basic(Email, Secret));

        credentials.Verify(s => s.TouchAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        clock.Now = clock.Now.AddHours(2);
        await AuthenticateAsync(Basic(Email, Secret));

        credentials.Verify(s => s.TouchAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TheDavPolicyChallengesBasicAndOnlyBasic()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSnoopyAuthentication()
            .BuildServiceProvider();

        var policy = await services.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(CardDavAuthenticationDefaults.PolicyName);

        Assert.NotNull(policy);
        // One scheme in the policy, one challenge emitted. Adding "Bearer" here would put a Bearer
        // challenge ahead of the Basic one on every 401 of /dav.
        Assert.Equal([CardDavAuthenticationDefaults.AuthenticationScheme], policy!.AuthenticationSchemes);
    }

    [Fact]
    public async Task TheDavSchemeIsRegisteredOnThisHandler()
    {
        var services = new ServiceCollection().AddLogging().AddSnoopyAuthentication().BuildServiceProvider();

        var scheme = await services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(CardDavAuthenticationDefaults.AuthenticationScheme);

        Assert.NotNull(scheme);
        Assert.Equal(typeof(CardDavAuthenticationHandler), scheme!.HandlerType);
    }

    [Fact]
    public void TheDefaultSchemesAreStillTheJwtOnes()
    {
        // A synchronisation secret must not open /api. The default schemes are what decides that.
        var services = new ServiceCollection().AddLogging().AddSnoopyAuthentication().BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultAuthenticateScheme);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultChallengeScheme);
    }

    /// <summary>Nothing was ever asked of the clock, so the refusal took a path that pays no delay.</summary>
    private void AssertNoDelayWasRequested() => Assert.Empty(clock.RequestedDelays);

    private void VerifyLogged(LogLevel level, Func<string, bool> matches) =>
        log.Verify(
            l => l.Log(
                level, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => matches(state.ToString()!)),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

    private void VerifyNeverLogged(Func<string, bool> matches) =>
        log.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => matches(state.ToString()!)),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
}
