using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class LoginController(
    IUserAuthenticator authenticator,
    IOptions<TokenConstants> tokenConstants,
    IMailCredentialStore credentialStore,
    IWebmailUserStore webmailUsers,
    ISessionGuard sessions,
    IImapConnectionPool pool,
    ILogger<LoginController> logger) : ApiBaseController
{
    /// <summary>
    /// Login with user credentials and cookie generation.
    /// </summary>
    /// <remarks>
    /// On success two cookies are issued: the JWT auth cookie, and an encrypted
    /// credentials cookie the mail endpoints need to open IMAP on the user's behalf.
    /// The password is unrecoverable from the database, so this is the only moment it
    /// can be captured.
    /// The JWT itself is never in the body — it exists only in the HttpOnly cookie, and the
    /// response carries just the session's inactivity window so the client can plan its renewal.
    /// </remarks>
    /// <param name="credentials">user credentials</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>The issued session's expiry, in minutes.</returns>
    /// <response code="200">Login successful</response>
    /// <response code="401">Invalid credentials</response>
    /// <response code="429">Too many authentication attempts</response>
    [HttpPost]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResultEnveloppe), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(Credentials credentials, CancellationToken cancellationToken)
    {
        Result<AuthToken> result = await authenticator.AuthenticateAsync(credentials.Email, credentials.Password, cancellationToken);

        if (result.IsFailure)
            return FromResult(result.ConvertFailure<LoginResponse>(), errorStatusCode: StatusCodes.Status401Unauthorized);

        if (!string.IsNullOrEmpty(result.Value.Token))
        {
            HttpContext.Response.WriteAuthCookie(tokenConstants.Value, result.Value.Token);

            // The only moment the 600k-iteration key derivation is affordable: the cookie carries
            // the result so no later request has to pay it again.
            //
            // The salt is fetched under the address the account was resolved to, never the one the
            // caller typed: the row this reads is the row RegisterLoginAsync just wrote, and a
            // spelling that missed it would hand back a salt nobody persisted — a key that opens
            // none of the connected accounts, for the whole life of the session.
            var salt = await webmailUsers.GetOrCreateKdfSaltAsync(result.Value.Email, cancellationToken);
            var kek = ConnectedAccountCipher.DeriveKek(credentials.Password, salt);

            credentialStore.Store(
                HttpContext.Response,
                new MailCredentialPayload(credentials.Password, kek),
                TimeSpan.FromMinutes(tokenConstants.Value.ExpiryInMinutes));
        }

        return Ok(new LoginResponse(result.Value.ExpiresIn));
    }

    /// <summary>
    /// Logs out a cookie authenticated user 
    /// </summary>
    /// <returns></returns>
    /// <response code="204">Logout successful</response>
    /// <response code="401">Try to logout an unauthenticated user</response>
    [Authorize]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult Logout()
    {
        HttpContext.Response.ClearAuthCookie(tokenConstants.Value);

        credentialStore.Clear(HttpContext.Response);

        pool.Close(AuthenticatedUser.WebmailUid);

        return NoContent();
    }

    /// <summary>
    /// Signs the account out everywhere, this device included.
    /// </summary>
    /// <remarks>
    /// The ordinary logout only clears this browser's cookies; the token itself stays valid until
    /// it expires, so a copy taken off the machine keeps working. This rotates the account's
    /// security stamp, which leaves every token already issued unable to match — the control to
    /// reach for when a session is believed to be in someone else's hands.
    /// It also refuses every pooled IMAP socket the account has out, so a session in someone else's
    /// hands loses the mailbox at once rather than at the socket's own expiry.
    /// </remarks>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Every session was revoked</response>
    /// <response code="401">Not authenticated</response>
    [Authorize]
    [HttpDelete("All")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> LogoutEverywhere(CancellationToken cancellationToken)
    {
        var email = AuthenticatedUser.Email;

        await webmailUsers.RotateSecurityStampAsync(email, cancellationToken);
        sessions.Forget(email);
        pool.Revoke(AuthenticatedUser.WebmailUid);

        HttpContext.Response.ClearAuthCookie(tokenConstants.Value);
        credentialStore.Clear(HttpContext.Response);

        logger.LogInformation("Audit: logout_everywhere email={Email} outcome=success", email);

        return NoContent();
    }
}
