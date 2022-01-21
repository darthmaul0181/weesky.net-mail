using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Authentication.Services;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class LoginController : ControllerBase
	{
		private IUserAuthenticator Authenticator { get; }
		private IOptions<TokenConstants> TokenConstants { get; }

		public LoginController(IUserAuthenticator authenticator, IOptions<TokenConstants> tokenConstants)
		{
			Authenticator = authenticator;
			TokenConstants = tokenConstants;
		}

		/// <summary>
		/// Login with user credentials and cookie generation.
		/// </summary>
		/// <param name="credentials">user credentials</param>
		/// <returns></returns>
		/// <response code="200">login successful</response>
		/// <response code="400">invalid credentials</response>
		[HttpPost]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> Login(Credentials credentials)
		{
			AuthResult result = Authenticator.Authenticate(credentials.Email, credentials.Password);

			if (result.IsSuccess)
			{
				HttpContext.Response.Cookies.Append(TokenConstants.Value.AuthCookieName, result.AccessToken.AccessToken);
				return Ok();
			}
			else
			{
				return BadRequest();
			}
		}

		/// <summary>
		/// Logs out a cookie authenticated user 
		/// </summary>
		/// <returns></returns>
		/// <response code="204">logout successful</response>
		/// <response code="401">try to logout an unauthenticated user</response>
		[Authorize]
		[HttpDelete]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> Logout()
		{
			HttpContext.Response.Cookies.Delete(TokenConstants.Value.AuthCookieName);

			return NoContent();
		}
	}
}
