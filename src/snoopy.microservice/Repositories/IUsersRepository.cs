using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories
{
    public interface IUsersRepository
    {
        Task<User> FindByEmailAsync(string email);
        Task<bool> IsValidPasswordAsync(User user, string password);
        Task<Result> ChangePasswordAsync(User user, string newPassword, string oldPassword);
        Task<Result> ChangeFullNameAsync(User user, string fullName);
        Task<Result<AccountInfo>> GetAccountInfoAsync(User user);
    }
}
