using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Providers.Weesky.Repositories;

namespace weesky.Snoopy.Providers.Weesky.Platform;

/// <summary>The dovecot row and the domains it owns; a mailbox the database does not hold is a
/// failure, which the controller answers 404.</summary>
internal sealed class WeeskyAccountInfoProvider(IUsersRepository users) : IAccountInfoProvider
{
    public Task<Result<AccountInfo>> GetAccountInfoAsync(User user, CancellationToken cancellationToken) =>
        users.GetAccountInfoAsync(user, cancellationToken);

    /// <summary>
    /// FindByEmailAsync answers null for a deleted *or* a deactivated mailbox, so this one read
    /// covers both — see <see cref="UsersRepository"/>.
    /// </summary>
    public async Task<bool> IsUsableAsync(string email, CancellationToken cancellationToken) =>
        await users.FindByEmailAsync(email, cancellationToken) is not null;
}
