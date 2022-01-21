using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Models;
using weesky.MailAdminRestAPI.Repositories;

namespace weesky.MailAdminRestAPI.Authentication.Services
{
	public class UserAuthenticator : IUserAuthenticator
	{
		private IUsersRepository _usersRepository;
		private ITokenManager _tokenManager { get; }

		public UserAuthenticator(IUsersRepository usersRepository, ITokenManager tokenManager)
		{ 
			_usersRepository = usersRepository;
			_tokenManager = tokenManager;
		}

		public AuthResult Authenticate(string email, string password)
		{
			User user = _usersRepository.FindByEmail(email);
			if (user == null)
			{
				return AuthResult.FailedResult;
			}

			if(!_usersRepository.IsValidPassword(user, password))
			{
				return AuthResult.FailedResult;
			}

			return new AuthResult
			{
				IsSuccess = true,
				AccessToken = _tokenManager.Generate(user)
			};
		}
	}
}
