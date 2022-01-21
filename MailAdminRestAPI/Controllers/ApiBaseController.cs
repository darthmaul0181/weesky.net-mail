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
	}
}
