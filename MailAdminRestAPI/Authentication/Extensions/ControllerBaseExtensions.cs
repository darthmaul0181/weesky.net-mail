using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using weesky.MailAdminRestAPI.Data;
using weesky.MailAdminRestAPI.Models;

namespace weesky.MailAdminRestAPI.Authentication.Extensions
{
	public static class ControllerBaseExtensions
	{
		public static User GetUser(this ControllerBase controller)
		{
			User user = null;
			IEnumerable<Claim> claims = controller.HttpContext?.User?.Claims ?? Enumerable.Empty<Claim>();

			if (claims.Any())
			{
				string name = claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Upn)?.Value;
				string domain = claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Dns)?.Value;

				if(!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(domain))
				{
					user = new User($"{name}@{domain}");
				}
			}

			return user;
		}
	}
}
