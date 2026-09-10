using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// Stands in for the real Dav handler under the real scheme name, issuing exactly the five claims
/// the real one issues (see DavAuthenticationHandler.FinishAsync): Upn, Dns, the webmail uid and
/// the two protocol switches. Everything downstream — the policy, AuthenticatedUser, the ownership check — runs
/// unchanged.
/// </summary>
internal sealed class TestDavAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DavTestUser user) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>What a request with no credentials carries here, since this handler holds no
    /// wire format of its own to omit.</summary>
    internal const string NoCredentialsHeader = "X-Test-No-Credentials";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(NoCredentialsHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var separator = user.Email.LastIndexOf('@');
        List<Claim> claims =
        [
            new(ClaimTypes.Upn, user.Email[..separator]),
            new(ClaimTypes.Dns, user.Email[(separator + 1)..]),
            new(WebmailClaimTypes.Uid, user.Uid.ToString()),
            new(WebmailClaimTypes.CardDav, user.CardDav ? "1" : "0"),
            new(WebmailClaimTypes.CalDav, user.CalDav ? "1" : "0"),
        ];

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
