using CSharpFunctionalExtensions;
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

		public Result<AuthToken> Authenticate(string email, string password)
		{
			User user = _usersRepository.FindByEmail(email);
			if (user == null)
			{
				return Result.Failure<AuthToken>("Authentication failed");
			}

			if(!_usersRepository.IsValidPassword(user, password))
			{
				return Result.Failure<AuthToken>("Authentication failed");
			}

			return Result.Success(_tokenManager.Generate(user));
		}
	}
}
