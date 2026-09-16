using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Providers.Weesky.Repositories;

namespace weesky.Scotty.Providers.Weesky.Platform;

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
