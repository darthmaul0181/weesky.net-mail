using Microsoft.AspNetCore.Mvc;

namespace weesky.MailAdminRestAPI.Models
{
	public class SecretChange
	{
		public string NewPassword { get; set; }
		public string OldPassword { get; set; }
	}
}
