using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Platform.Generic;

/// <summary>
/// No platform to ask: nothing outside the mailbox can say which addresses the account owns, so
/// ownership is not enforced at all and the alias list is empty. The sending identities the user
/// declares become the only set — the SMTP server is what finally refuses a sender it will not relay.
/// </summary>
internal sealed class FreeIdentityDirectory : IAliasDirectory
{
    public bool EnforcesOwnership => false;

    public Task<IReadOnlyList<string>> GetAddressesAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
