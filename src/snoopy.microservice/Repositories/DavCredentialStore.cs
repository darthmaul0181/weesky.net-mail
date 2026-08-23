using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class DavCredentialStore(PreferencesDbContext context) : IDavCredentialStore
{
    public async Task<DavCredentialState> GetStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await context.DavCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        return row is null
            ? new DavCredentialState(false, false, null)
            : new DavCredentialState(true, row.CardDavEnabled, row.LastUsedAt);
    }

    public async Task<DavCredentialRecord?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await context.DavCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        return row is null ? null : new DavCredentialRecord(row.CardDavEnabled, row.SecretHash, row.Salt);
    }

    public async Task<string?> EnableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await Track(userId, cancellationToken);
        if (existing is not null)
        {
            existing.CardDavEnabled = true;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var secret = DavSecret.Generate();
        var salt = DavSecret.NewSalt();
        var row = new DavCredential
        {
            UserId = userId,
            CardDavEnabled = true,
            Salt = salt,
            SecretHash = DavSecret.Hash(salt, secret),
            CreatedAt = DateTime.UtcNow
        };
        context.DavCredentials.Add(row);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return secret;
        }
        catch (DbUpdateException)
        {
            // A concurrent first enable — double click, two tabs — inserted the same key. The
            // first secret written wins; this call answers as a plain re-enable would, so no
            // second secret is ever handed out and neither request dies on the primary key.
            context.Entry(row).State = EntityState.Detached;
            var winner = await Track(userId, cancellationToken);
            if (winner is null) throw;

            winner.CardDavEnabled = true;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
    }

    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        row.CardDavEnabled = false;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> RegenerateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return null;

        var secret = DavSecret.Generate();
        // The salt goes with it: keeping it would make a regeneration a half-done rotation.
        row.Salt = DavSecret.NewSalt();
        row.SecretHash = DavSecret.Hash(row.Salt, secret);
        await context.SaveChangesAsync(cancellationToken);

        return secret;
    }

    public async Task TouchAsync(Guid userId, DateTime usedAt, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        row.LastUsedAt = usedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        context.DavCredentials.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task<DavCredential?> Track(Guid userId, CancellationToken cancellationToken) =>
        context.DavCredentials.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
}
