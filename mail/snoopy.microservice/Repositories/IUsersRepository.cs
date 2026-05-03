using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
	public interface IUsersRepository
	{
		User FindByEmail(string email);
		bool IsValidPassword(User user, string password);
		Result ChangePassword(User user, string newPassword, string oldPassword);
		Result ChangeFullName(User user, string fullName);
		Result<AccountInfo> GetAccountInfo(User user);
	}
}