using CSharpFunctionalExtensions;
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
		[HttpPost]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<AuthToken>> Login(Credentials credentials)
		{
			Result<AuthToken> result = _authenticator.Authenticate(credentials.Email, credentials.Password);

			if (result.IsSuccess)
			{
				HttpContext.Response.Cookies.Append(_tokenConstants.Value.AuthCookieName, result.Value.Token);
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
			HttpContext.Response.Cookies.Delete(_tokenConstants.Value.AuthCookieName);

			return NoContent();
		}
	}
}
