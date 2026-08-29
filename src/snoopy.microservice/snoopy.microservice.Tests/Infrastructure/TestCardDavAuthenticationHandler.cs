using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// Stands in for the real CardDav handler under the real scheme name, issuing exactly the three
/// claims the real one issues (see CardDavAuthenticationHandler.FinishAsync): Upn, Dns and the
/// webmail uid. Everything downstream — the policy, AuthenticatedUser, the ownership check — runs
/// unchanged.
/// </summary>
internal sealed class TestCardDavAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DavTestUser user) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var separator = user.Email.LastIndexOf('@');
        List<Claim> claims =
        [
            new(ClaimTypes.Upn, user.Email[..separator]),
            new(ClaimTypes.Dns, user.Email[(separator + 1)..]),
            new(WebmailClaimTypes.Uid, user.Uid.ToString()),
        ];

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
