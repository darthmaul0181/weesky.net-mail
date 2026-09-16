using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Providers.Weesky.Repositories;

namespace weesky.Scotty.Providers.Weesky.Platform;

/// <summary>
/// The weesky.net platform administers the mailbox, so its alias table is the ownership rule the
/// whole sending-identity model is judged against.
/// </summary>
internal sealed class WeeskyAliasDirectory(IAliasesRepository aliases) : IAliasDirectory
{
    public bool EnforcesOwnership => true;

    public async Task<IReadOnlyList<string>> GetAddressesAsync(User user, CancellationToken cancellationToken) =>
        (await aliases.GetAliasesAsync(user, cancellationToken)).ToAddresses();
}
