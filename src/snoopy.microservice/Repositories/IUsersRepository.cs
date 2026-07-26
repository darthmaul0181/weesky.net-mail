using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IUsersRepository
{
    Task<User?> FindByEmailAsync(string email);

    /// <summary>
    /// Checks an email and password together. One call rather than a lookup followed by a
    /// password check: splitting them made an unknown address cheaper to answer than a wrong
    /// password, which is enough to enumerate the mailboxes that exist.
    /// </summary>
    Task<CredentialCheck> VerifyCredentialsAsync(string email, string password);
    Task<Result> ChangePasswordAsync(User user, string newPassword, string oldPassword);
    Task<Result> ChangeFullNameAsync(User user, string fullName);
    Task<Result<AccountInfo>> GetAccountInfoAsync(User user);
}
