using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The addresses a user answers to, lower-cased and without duplicates. Two readings: the one the
/// DAV principal publishes as <c>calendar-user-address-set</c> (5c, décision 5 — the home
/// address, its other domains, every stored identity), and the one an invitation is matched
/// against for ONE mail account (5e, décision 2 — the home list with the home account's own
/// identities, or a connected account's login and its own identities).
/// </summary>
public interface IUserAddresses
{
    Task<IReadOnlyList<string>> ForAccountAsync(User user, MailAccountConnection connection, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken);
}

internal sealed class UserAddresses(
    IAccountInfoProvider accounts, ISendingIdentityStore identities, ILogger<UserAddresses> logger) : IUserAddresses
{
    public async Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken) =>
        Distinct(await HomeAsync(user, cancellationToken),
            (await identities.GetAllAsync(user.WebmailUid, cancellationToken)).Select(i => i.Address));

    public async Task<IReadOnlyList<string>> ForAccountAsync(
        User user, MailAccountConnection connection, CancellationToken cancellationToken)
    {
        var own = (await identities.GetAsync(user.WebmailUid, connection.StorageAccountId, cancellationToken))
            .Select(i => i.Address);
        return connection.AccountId == MailAccountConnection.Primary
            ? Distinct(await HomeAsync(user, cancellationToken), own)
            : Distinct([connection.Username], own);
    }

    /// <summary>The home address first, then the same name on the account's other domains. A
    /// platform that cannot answer is a warning and the home address alone — never a failure.</summary>
    private async Task<List<string>> HomeAsync(User user, CancellationToken cancellationToken)
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
