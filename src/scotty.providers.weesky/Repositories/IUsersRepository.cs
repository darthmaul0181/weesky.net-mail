using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Providers.Weesky.Repositories;

public interface IUsersRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<Result> ChangePasswordAsync(User user, string newPassword, string oldPassword, CancellationToken cancellationToken);
    Task<Result> ChangeFullNameAsync(User user, string fullName, CancellationToken cancellationToken);
    Task<Result<AccountInfo>> GetAccountInfoAsync(User user, CancellationToken cancellationToken);
}
