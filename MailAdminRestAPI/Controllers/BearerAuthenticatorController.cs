using Microsoft.AspNetCore.Mvc;
using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Authentication.Services;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class BearerAuthenticatorController : ControllerBase
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
		/// <response code="200">logout successful</response>
		/// <response code="400">logout successful</response>
		[HttpPost]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<AuthResult> Authenticate(Credentials credentials)
		{
			AuthResult result = Authenticator.Authenticate(credentials.Email, credentials.Password);

			return StatusCode(result.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, result);
		}
	}
}
