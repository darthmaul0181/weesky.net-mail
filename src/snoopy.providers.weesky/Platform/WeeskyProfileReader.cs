using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Providers.Weesky.Repositories;

namespace weesky.Snoopy.Providers.Weesky.Platform;

/// <summary>The display name is the dovecot row's FullName — read from the database, never the JWT
/// claims, so a name changed from the Account tab shows on the next send.</summary>
internal sealed class WeeskyProfileReader(IUsersRepository users) : IProfileReader
{
    public async Task<string?> GetDisplayNameAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return (await users.FindByEmailAsync(user.Email, cancellationToken))?.FullName;
    }
}
