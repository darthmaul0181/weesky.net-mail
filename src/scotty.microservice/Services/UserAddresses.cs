using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Microservice.Repositories;

namespace weesky.Scotty.Microservice.Services;

/// <summary>
/// The addresses a user answers to, lower-cased and without duplicates. Three readings: the one the
/// DAV principal publishes as <c>calendar-user-address-set</c> (5c, décision 5 — the home
/// address, its other domains, every stored identity), the one an invitation is matched
/// against for ONE mail account (5e, décision 2 — the home list with the home account's own
/// identities, or a connected account's login and its own identities), and the organizer's own
/// (5e, décision 8 — the primary account's reading: a connected account is somebody else).
/// </summary>
public interface IUserAddresses
{
    Task<IReadOnlyList<string>> ForAccountAsync(User user, MailAccountConnection connection, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ForPrimaryAsync(User user, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken);
}

internal sealed class UserAddresses(
    IAccountInfoProvider accounts, ISendingIdentityStore identities, ILogger<UserAddresses> logger) : IUserAddresses
{
    private (Guid Uid, Task<List<string>> Addresses)? home;

    public async Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken) =>
        Distinct(await HomeAsync(user, cancellationToken),
            (await identities.GetAllAsync(user.WebmailUid, cancellationToken)).Select(i => i.Address));

    public async Task<IReadOnlyList<string>> ForAccountAsync(
        User user, MailAccountConnection connection, CancellationToken cancellationToken) =>
        connection.AccountId == MailAccountConnection.Primary
            ? await ForPrimaryAsync(user, cancellationToken)
            : Distinct([connection.Username], await IdentitiesOfAsync(user, connection.StorageAccountId, cancellationToken));

    public async Task<IReadOnlyList<string>> ForPrimaryAsync(User user, CancellationToken cancellationToken) =>
        Distinct(await HomeAsync(user, cancellationToken), await IdentitiesOfAsync(user, AccountScope.Primary, cancellationToken));

    private async Task<IEnumerable<string>> IdentitiesOfAsync(User user, string accountId, CancellationToken cancellationToken) =>
        (await identities.GetAsync(user.WebmailUid, accountId, cancellationToken)).Select(i => i.Address);

    /// <summary>Read once per request, the service being scoped: a detail reads two lists, a save and its hook the same one twice.</summary>
    private Task<List<string>> HomeAsync(User user, CancellationToken cancellationToken)
    {
        if (home is not { } read || read.Uid != user.WebmailUid) home = read = (user.WebmailUid, ReadHomeAsync(user, cancellationToken));
        return read.Addresses;
    }

    /// <summary>The home address first, then the same name on the account's other domains. A
    /// platform that cannot answer is a warning and the home address alone — never a failure.</summary>
    private async Task<List<string>> ReadHomeAsync(User user, CancellationToken cancellationToken)
    {
        List<string> addresses = [user.Email];
        var account = await accounts.GetAccountInfoAsync(user, cancellationToken);
        if (account.IsSuccess)
            addresses.AddRange(account.Value.Domains
                .Where(domain => !string.Equals(domain.Name, user.Domain, StringComparison.OrdinalIgnoreCase))
                .Select(domain => $"{user.Name}@{domain.Name}"));
        else
            logger.LogWarning(
                "The account's domains were unavailable ({Reason}); the address list holds the primary address alone",
                account.Error);
        return addresses;
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> first, IEnumerable<string> then) =>
        [.. first.Concat(then).Select(a => a.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal)];
}
