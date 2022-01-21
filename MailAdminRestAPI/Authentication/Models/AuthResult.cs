namespace weesky.MailAdminRestAPI.Authentication.Models
{
	public class AuthResult
	{
		public bool IsSuccess { get; set; }
		public AuthToken AccessToken { get; set; }
		public static AuthResult FailedResult { get; } = new AuthResult();
	}
}
