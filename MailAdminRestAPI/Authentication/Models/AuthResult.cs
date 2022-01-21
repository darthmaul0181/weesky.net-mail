namespace weesky.MailAdminRestAPI.Authentication.Models
{
	public class AuthResult
	{
		/// <summary>
		/// Indicates if the authentication has succeeded.
		/// </summary>
		public bool IsSuccess { get; set; }

		/// <summary>
		/// The Authentication token.
		/// </summary>
		public AuthToken AccessToken { get; set; }

		/// <summary>
		/// Failed authentication.
		/// </summary>
		public static AuthResult FailedResult { get; } = new AuthResult();
	}
}
