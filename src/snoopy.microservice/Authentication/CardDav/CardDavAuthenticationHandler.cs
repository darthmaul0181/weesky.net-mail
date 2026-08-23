using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// Basic over TLS, carrying the synchronisation secret. The order of its checks is the
/// specification, not an implementation detail — see the slice's design note:
///
/// transport, then throttle, both without reading anything; then the account, its usability, its
/// row and its digest; and only then the switch. Answering 403 on a switched-off account before
/// comparing the digest would make the response an account-enumeration oracle.
/// </summary>
internal sealed class CardDavAuthenticationHandler(
    IOptionsMonitor<CardDavAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IDavCredentialStore credentials,
    IWebmailUserStore users,
    IAccountInfoProvider accounts,
    IDavAuthenticationCache cache,
    AuthAttemptThrottle throttle,
    TimeProvider clock,
    IHostEnvironment environment)
    : AuthenticationHandler<CardDavAuthenticationOptions>(options, loggerFactory, encoder)
{
    private const string OutcomeKey = "carddav-auth-outcome";
    private const string RetryAfterKey = "carddav-auth-retry-after";
    private const string BasicPrefix = "Basic ";

    private enum Outcome { Unauthorized, Forbidden, TooManyRequests }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadBasic(out var identifier, out var secret))
        {
            // No Basic header — absent, or another scheme, Bearer above all: the JWT is a
            // first-class scheme here, which is what keeps /dav reachable from Swagger, curl and an
            // ordinary webmail session. A malformed *Basic* header is an attempt, and answers as one.
            return HasBasicHeader()
                ? Refuse(Outcome.Unauthorized)
                : await Context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        }

        // Basic carries the secret in clear: outside TLS one PROPFIND hands it to whoever listens,
        // and a secret opening the whole address book does not replay once, it replays until it is
        // revoked. Read off Request.Scheme, which UseForwardedHeaders has already corrected from
        // X-Forwarded-Proto — Request.IsHttps is always false behind the proxy.
        if (!Request.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !environment.IsDevelopment())
        {
            Logger.LogWarning("CardDAV authentication refused: request origin is not https");
            return Refuse(Outcome.Forbidden);
        }

        // The same address the login rate limiter partitions on, and only meaningful because
        // UseForwardedHeaders runs first in the pipeline: behind the proxy every caller would
        // otherwise share the proxy's own key.
        var address = Context.Connection.RemoteIpAddress?.ToString();
        if (throttle.IsBlocked(identifier, address, out var retryAfter))
        {
            Context.Items[RetryAfterKey] = retryAfter;
            return Refuse(Outcome.TooManyRequests);
        }

        var canonical = identifier.Trim().ToLowerInvariant();
        var fingerprint = DavSecret.Fingerprint(secret);

        if (cache.TryGet(canonical, fingerprint, out var cached))
            return await FinishAsync(canonical, fingerprint, cached, cachedHit: true);

        var account = await users.FindByEmailAsync(canonical, Context.RequestAborted);
        if (account is null) return await RefuseWithDelayAsync(canonical, address);

        // The same check the JWT path runs through ISessionGuard: a deleted or disabled account
        // must not keep synchronising, and forgetting it would make the address book the last open
        // door of a closed account. The security stamp does not apply — a secret is not a session.
        if (!await accounts.IsUsableAsync(canonical, Context.RequestAborted))
            return await RefuseWithDelayAsync(canonical, address);

        var row = await credentials.FindAsync(account.Value.Id, Context.RequestAborted);
        if (row is null) return await RefuseWithDelayAsync(canonical, address);

        if (!DavSecret.Matches(row.Value.Salt, row.Value.SecretHash, secret))
        {
            // A stored digest of the wrong width is a storage fault, indistinguishable from a wrong
            // secret at Matches, which cannot log by constraint. The answer is 401 either way; this
            // line says which it was, on the GUID alone — never the address, never the secret.
            if (row.Value.SecretHash.Length != DavSecret.HashLength)
                Logger.LogError("CardDAV credential row of {UserId} holds a malformed secret hash", account.Value.Id);

            return await RefuseWithDelayAsync(canonical, address);
        }

        var identity = new DavIdentity(account.Value.Id, row.Value.CardDavEnabled);
        return await FinishAsync(canonical, fingerprint, identity, cachedHit: false);
    }

    /// <summary>
    /// Three responses out of one override: the framework routes every failed authentication here,
    /// forbidden and throttled included, and the marker says which one this was.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties? properties)
    {
        var outcome = Context.Items.TryGetValue(OutcomeKey, out var stored) && stored is Outcome value
            ? value
            : Outcome.Unauthorized;

        switch (outcome)
        {
            case Outcome.Forbidden:
                // No named precondition and no challenge: these two refusals precede the protocol.
                Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case Outcome.TooManyRequests:
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var retryAfter = Context.Items.TryGetValue(RetryAfterKey, out var left) && left is TimeSpan span
                    ? span
                    : AuthAttemptThrottle.Window;
                Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                break;

            default:
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                // Without this header a client has no reason to send credentials and loops on the
                // failure. The realm never varies: it is a keychain key on the client side.
                Response.Headers.WWWAuthenticate = $"Basic realm=\"{CardDavAuthenticationDefaults.Realm}\"";
                break;
        }

        return Task.CompletedTask;
    }

    private bool HasBasicHeader() =>
        Request.Headers.Authorization.ToString().StartsWith(BasicPrefix, StringComparison.OrdinalIgnoreCase);

    private bool TryReadBasic(out string identifier, out string secret)
    {
        identifier = string.Empty;
        secret = string.Empty;

        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BasicPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        // Four base64 characters carry three bytes, and internal whitespace only shortens that.
        var payload = header[BasicPrefix.Length..].Trim();
        var decoded = new byte[payload.Length / 4 * 3];
        if (!Convert.TryFromBase64String(payload, decoded, out var written)) return false;

        var pair = Encoding.UTF8.GetString(decoded[..written]);
        var separator = pair.IndexOf(':');
        if (separator <= 0) return false;

        identifier = pair[..separator];
        secret = pair[(separator + 1)..];
        return identifier.Length > 0 && secret.Length > 0;
    }

    private AuthenticateResult Refuse(Outcome outcome)
    {
        Context.Items[OutcomeKey] = outcome;
        return AuthenticateResult.Fail(outcome.ToString());
    }

    /// <summary>
    /// The random delay Radicale applies, for the two signals a bare response time gives away: the
    /// existence of the account, and the cost of guessing. Task.Delay and never Thread.Sleep — a
    /// blocking wait would turn the speed bump into the pool exhaustion it exists to prevent — and
    /// no lock, no open connection is held across it. Routed through the injected clock, so a test
    /// observes the wait it asks for instead of chronometering one.
    /// </summary>
    private async Task<AuthenticateResult> RefuseWithDelayAsync(string identifier, string? address)
    {
        throttle.RecordFailure(identifier, address);
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(500, 1501)),
                clock, Context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // A client hanging up mid-delay is still a refusal: HandleAuthenticateOnceAsync does
            // not swallow, so an escaping cancellation would surface as a pipeline error instead
            // of the 401 this path was already heading for. The failure is recorded above.
        }

        return Refuse(Outcome.Unauthorized);
    }

    private async Task<AuthenticateResult> FinishAsync(
        string identifier, string fingerprint, DavIdentity identity, bool cachedHit)
    {
        if (!cachedHit) cache.Store(identifier, fingerprint, identity);
        throttle.RecordSuccess(identifier);

        // After the digest matched and never before: a 403 answered earlier would say "this
        // account exists and its DAV is asleep" to anyone asking.
        if (!identity.CardDavEnabled) return Refuse(Outcome.Forbidden);

        if (cache.ShouldTouch(identity.UserId))
            await credentials.TouchAsync(identity.UserId, clock.GetUtcNow().UtcDateTime, Context.RequestAborted);

        var separator = identifier.LastIndexOf('@');
        var claims = new List<Claim>
        {
            new(ClaimTypes.Upn, identifier[..separator]),
            new(ClaimTypes.Dns, identifier[(separator + 1)..]),
            new(WebmailClaimTypes.Uid, identity.UserId.ToString())
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CardDavAuthenticationDefaults.AuthenticationScheme));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
