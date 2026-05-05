using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Services
{
	public class UserAuthenticator : IUserAuthenticator
	{
		private readonly IUsersRepository _usersRepository;
		private readonly ITokenManager _tokenManager;
		private readonly ILogger<UserAuthenticator> _logger;

		public UserAuthenticator(IUsersRepository usersRepository, ITokenManager tokenManager, ILogger<UserAuthenticator> logger)
		{
			_usersRepository = usersRepository;
			_tokenManager = tokenManager;
			_logger = logger;
		}

		public Result<AuthToken> Authenticate(string email, string password)
		{
			User user = _usersRepository.FindByEmail(email);
			if (user == null)
			{
				_logger.LogInformation("Audit: login email={Email} outcome=failure reason=unknown_user", email);
				return Result.Failure<AuthToken>("Authentication failed");
			}

			if(!_usersRepository.IsValidPassword(user, password))
			{
				_logger.LogInformation("Audit: login email={Email} outcome=failure reason=bad_password", email);
				return Result.Failure<AuthToken>("Authentication failed");
			}

			_logger.LogInformation("Audit: login email={Email} outcome=success", email);
			return Result.Success(_tokenManager.Generate(user));
		}
	}
}
