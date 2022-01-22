using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using weesky.MailAdminRestAPI.Authentication.Extensions;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Controllers
{
	public class ApiBaseController : ControllerBase
	{
		/// <summary>
		/// The authenticated user (JWT or Cookie authenticated).
		/// </summary>
		public User AuthenticatedUser => this.GetUser();

		protected ActionResult<ResultEnveloppe> FromResult(Result result, int errorStatusCode = StatusCodes.Status400BadRequest, int successStatusCode = StatusCodes.Status200OK)
		{
			if (result.IsSuccess) return StatusCode(successStatusCode);
			return StatusCode(errorStatusCode, ResultEnveloppe.CrateErrorEnveloppe(result.Error));
		}

		protected ActionResult<T> FromResult<T>(Result<T> result, int errorStatusCode = StatusCodes.Status400BadRequest)
		{
			if (result.IsSuccess) return Ok(result.Value);
			return StatusCode(errorStatusCode);
		}
	}
}
