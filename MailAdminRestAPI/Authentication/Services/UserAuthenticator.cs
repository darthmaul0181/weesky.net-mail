using CryptSharp.Core;
using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Services;

namespace weesky.MailAdminRestAPI.Authentication.Services
{
	public class UserAuthenticator : IUserAuthenticator
	{
		private IRepository UserRepository { get; }
		private ITokenManager TokenManager { get; }

		public UserAuthenticator(IRepository userRepository, ITokenManager tokenManager)
		{
			UserRepository = userRepository;
			TokenManager = tokenManager;
		}

		public AuthResult Authenticate(string email, string password)
		{
			User user = UserRepository.FindByEmail(email);
			if (user == null)
			{
				return AuthResult.FailedResult;
			}

			if(!UserRepository.IsValidPassword(user, password))
			{
				return AuthResult.FailedResult;
			}

			return new AuthResult
			{
				IsSuccess = true,
				AccessToken = TokenManager.Generate(user)
			};
		}
	}
}
