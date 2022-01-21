namespace weesky.MailAdminRestAPI.Authentication.Models
{
	public class AuthToken
	{
		public long ExpiresIn { get; set; }
		public string? AccessToken { get; set; }
	}
}
