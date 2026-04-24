using System.ComponentModel.DataAnnotations;

namespace weesky.MailAdminRestAPI.Models
{
	public class SecretChange
	{
		[Required]
		[StringLength(256, MinimumLength = 8)]
		public string NewPassword { get; set; }

		[Required]
		[StringLength(256, MinimumLength = 1)]
		public string OldPassword { get; set; }
	}
}
