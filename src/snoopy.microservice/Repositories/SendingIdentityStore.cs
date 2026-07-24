using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class SendingIdentityStore(PreferencesDbContext context) : ISendingIdentityStore
{
    public async Task<IReadOnlyList<SendingIdentity>> GetAsync(string accountId, CancellationToken cancellationToken)
        => await context.SendingIdentities.AsNoTracking()
            .Where(i => i.AccountId == accountId)
            .OrderBy(i => i.Address)
            .ToListAsync(cancellationToken);

    public async Task ReplaceAsync(string accountId, IReadOnlyList<SendingIdentity> identities, CancellationToken cancellationToken)
    {
        var existing = await context.SendingIdentities
            .Where(i => i.AccountId == accountId)
            .ToListAsync(cancellationToken);
        context.SendingIdentities.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var identity in identities)
        {
            identity.AccountId = accountId;
            identity.UpdatedAt = now;
            context.SendingIdentities.Add(identity);
        }

        // A single SaveChanges: on a relational provider this commits as one transaction.
        await context.SaveChangesAsync(cancellationToken);
    }
}
