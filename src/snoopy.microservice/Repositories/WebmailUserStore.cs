using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class WebmailUserStore(
    PreferencesDbContext context, IDavAuthenticationCache davCache) : IWebmailUserStore
{
    public async Task<WebmailAccount> RegisterLoginAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var now = DateTime.UtcNow;

        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);
        if (existing is not null)
        {
            existing.LastLoginDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return Account(existing);
        }

        var row = new WebmailUser
        {
            Id = Guid.NewGuid(),
            Email = canonical,
            SecurityStamp = Guid.NewGuid(),
            CreationDate = now,
            LastLoginDate = now
        };
        context.Users.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Account(row);
        }
        catch (DbUpdateException)
        {
            // A concurrent first login inserted the same email; adopt the winner's row.
            context.Entry(row).State = EntityState.Detached;
            var winner = await context.Users
                .FirstAsync(u => u.Email == canonical, cancellationToken);
            winner.LastLoginDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return Account(winner);
        }
    }

    public async Task<WebmailAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var row = await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);

        return row is null ? null : Account(row);
    }

    public async Task<Guid> RotateSecurityStampAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var row = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);

        // No row means no token was ever issued for this account, so there is nothing to revoke.
        // Answering with a fresh value anyway keeps the caller's contract simple, and one that
        // matches nothing stored is refused on the next request rather than trusted.
        if (row is null) return Guid.NewGuid();

        row.SecurityStamp = Guid.NewGuid();

        // Destroyed rather than switched off, and in the rotation's own SaveChanges so the two are
        // one transaction. Not IDavCredentialStore.DeleteAsync: it carries a SaveChanges of its
        // own, which would make two.
        var secret = await context.DavCredentials
            .FirstOrDefaultAsync(c => c.UserId == row.Id, cancellationToken);
        if (secret is not null) context.DavCredentials.Remove(secret);

        await context.SaveChangesAsync(cancellationToken);

        // After the commit, never before: from here no request can still read the deleted row, so
        // the eviction races only against reads already in flight.
        davCache.Forget(canonical);

        return row.SecurityStamp;
    }

    public async Task<byte[]> GetOrCreateKdfSaltAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var row = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);

        // No row means no connected account can exist either — the FK forbids one — so a value
        // that goes nowhere is harmless, and the caller still gets a usable salt.
        if (row is null) return ConnectedAccountCipher.NewSalt();

        if (row.KdfSalt is { Length: ConnectedAccountCipher.SaltLength }) return row.KdfSalt;

        row.KdfSalt = ConnectedAccountCipher.NewSalt();
        await context.SaveChangesAsync(cancellationToken);

        return row.KdfSalt;
    }

    public async Task DeleteByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);
        if (existing is null) return;

        context.Users.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);

        // The cascade takes the dav_credentials row, but not the burst entry: without this the
        // deleted account's secret keeps opening the address book for the rest of the window.
        davCache.Forget(canonical);
    }

    private static WebmailAccount Account(WebmailUser row) => new(row.Id, row.SecurityStamp);

    private static string Canonical(string email) => email.Trim().ToLowerInvariant();
}
