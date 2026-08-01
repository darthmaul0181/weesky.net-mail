using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class SendingIdentityStore(PreferencesDbContext context) : ISendingIdentityStore
{
    public async Task<IReadOnlyList<SendingIdentity>> GetAsync(
        Guid userId, string accountId, CancellationToken cancellationToken)
        => await context.SendingIdentities.AsNoTracking()
            .Where(i => i.UserId == userId && i.AccountId == accountId)
            .OrderBy(i => i.Address)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SendingIdentity>> GetAllAsync(
        Guid userId, CancellationToken cancellationToken)
        => await context.SendingIdentities.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.AccountId).ThenBy(i => i.Address)
            .ToListAsync(cancellationToken);

    public async Task ReplaceAsync(Guid userId, string accountId,
        IReadOnlyList<SendingIdentity> identities, CancellationToken cancellationToken)
    {
        var existing = await context.SendingIdentities
            .Where(i => i.UserId == userId && i.AccountId == accountId)
            .ToListAsync(cancellationToken);
        context.SendingIdentities.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var identity in identities)
        {
            identity.UserId = userId;
            identity.AccountId = accountId;
            identity.UpdatedAt = now;
            context.SendingIdentities.Add(identity);
        }

        // A single SaveChanges: on a relational provider this commits as one transaction.
        await context.SaveChangesAsync(cancellationToken);
    }
}
