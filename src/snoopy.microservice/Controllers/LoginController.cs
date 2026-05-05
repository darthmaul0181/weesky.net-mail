using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class LoginController : ApiBaseController
	{
		private IUserAuthenticator _authenticator;
		private IOptions<TokenConstants> _tokenConstants;

		public LoginController(IUserAuthenticator authenticator, IOptions<TokenConstants> tokenConstants)
		{
			_authenticator = authenticator;
			_tokenConstants = tokenConstants;
		}

		/// <summary>
		/// Login with user credentials and cookie generation.
		/// </summary>
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
			Result<AuthToken> result = _authenticator.Authenticate(credentials.Email, credentials.Password);

			if (result.IsSuccess)
			{
				HttpContext.Response.Cookies.Append(_tokenConstants.Value.AuthCookieName, result.Value.Token, new CookieOptions
				{
					HttpOnly = true,
					Secure = true,
					SameSite = SameSiteMode.Strict,
					Expires = DateTimeOffset.UtcNow.AddMinutes(_tokenConstants.Value.ExpiryInMinutes)
				});
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
		public async Task<ActionResult> Logout()
		{
			HttpContext.Response.Cookies.Delete(_tokenConstants.Value.AuthCookieName, new CookieOptions
			{
				HttpOnly = true,
				Secure = true,
				SameSite = SameSiteMode.Strict
			});

			return NoContent();
		}
	}
}
