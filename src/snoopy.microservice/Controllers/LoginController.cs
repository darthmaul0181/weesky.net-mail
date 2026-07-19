using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

[Route("api/[controller]")]
[ApiController]
public sealed class LoginController : ApiBaseController
{
    private readonly IUserAuthenticator _authenticator;
    private readonly IOptions<TokenConstants> _tokenConstants;
    private readonly IMailCredentialStore _credentialStore;

    public LoginController(
        IUserAuthenticator authenticator,
        IOptions<TokenConstants> tokenConstants,
        IMailCredentialStore credentialStore)
    {
        _authenticator = authenticator;
        _tokenConstants = tokenConstants;
        _credentialStore = credentialStore;
    }

    /// <summary>
    /// Login with user credentials and cookie generation.
    /// </summary>
    /// <remarks>
    /// On success two cookies are issued: the JWT auth cookie, and an encrypted
    /// credentials cookie the mail endpoints need to open IMAP on the user's behalf.
    /// The password is unrecoverable from the database, so this is the only moment it
    /// can be captured.
    /// </remarks>
    /// <param name="credentials">user credentials</param>
    /// <returns></returns>
    /// <response code="200">Login successful</response>
    /// <response code="401">Invalid credentials</response>
    /// <response code="429">Too many authentication attempts</response>
    [HttpPost]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthToken>> Login(Credentials credentials)
    {
        Result<AuthToken> result = await _authenticator.AuthenticateAsync(credentials.Email, credentials.Password);

        if (result.IsSuccess)
        {
            HttpContext.Response.Cookies.Append(_tokenConstants.Value.AuthCookieName, result.Value.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddMinutes(_tokenConstants.Value.ExpiryInMinutes)
            });

            _credentialStore.Store(
                HttpContext.Response,
                credentials.Password,
                TimeSpan.FromMinutes(_tokenConstants.Value.ExpiryInMinutes));
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status401Unauthorized);
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
        HttpContext.Response.Cookies.Delete(_tokenConstants.Value.AuthCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

        _credentialStore.Clear(HttpContext.Response);

        return NoContent();
    }
}
