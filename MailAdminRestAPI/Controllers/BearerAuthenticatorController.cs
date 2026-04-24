using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Authentication.Services;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class BearerAuthenticatorController : ApiBaseController
	{
		private IUserAuthenticator Authenticator { get; }

		public BearerAuthenticatorController(IUserAuthenticator authenticator)
		{
			Authenticator = authenticator;
		}

		/// <summary>
		/// Generates a Json Web Token (JWT) for bearer authentication.
		/// </summary>
		/// <param name="credentials">user credentials</param>
		/// <returns></returns>
		/// <response code="200">User authentication succeeded</response>
		/// <response code="401">Wrong credentials</response>
		/// <response code="429">Too many authentication attempts</response>
		[HttpPost]
		[EnableRateLimiting("login")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		public ActionResult<AuthToken> Authenticate(Credentials credentials)
		{
			Result<AuthToken> result = Authenticator.Authenticate(credentials.Email, credentials.Password);
			return FromResult(result, StatusCodes.Status401Unauthorized);
		}
	}
}
