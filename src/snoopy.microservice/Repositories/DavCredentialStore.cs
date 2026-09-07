using System.Data;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class DavCredentialStore(PreferencesDbContext context) : IDavCredentialStore
{
    public async Task<DavCredentialState> GetStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Projected in the query: the digest and the salt never leave MariaDB on the one path
        // whose whole point is that the screen does not see them.
        var row = await context.DavCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.CardDavEnabled, c.CalDavEnabled, c.LastUsedAt })
            .FirstOrDefaultAsync(cancellationToken);

        // MariaDB DATETIME carries no zone, so Pomelo reads back Unspecified and the serialiser
        // then emits no "Z" — which a browser reads as local time, shifting a relative "last used"
        // by the viewer's offset. The column is written in UTC; this says so on the way out.
        return row is null
            ? new DavCredentialState(false, false, false, null)
            : new DavCredentialState(true, row.CardDavEnabled, row.CalDavEnabled, AsUtc(row.LastUsedAt));
    }

    public async Task<DavCredentialRecord?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await context.DavCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        return row is null
            ? null
            : new DavCredentialRecord(row.CardDavEnabled, row.CalDavEnabled, row.SecretHash, row.Salt);
    }

    /// <summary>
    /// The transaction is opened INSIDE the execution strategy and never around it — a retrying
    /// strategy refuses one the caller opened.
    ///
    /// READ COMMITTED, and it is not a preference: under MariaDB's REPEATABLE READ the snapshot is
    /// taken at the first read, so the row a rival commits inside the race window stays invisible.
    /// The duplicate-key catch below — and EnsureDefaultAsync's, which runs here as
    /// <paramref name="alongside"/> — would then read no winner and rethrow, turning a double click
    /// into a 500. The InMemory provider has neither transaction nor isolation and cannot see this.
    /// </summary>
    public Task<string?> EnableAsync(Guid userId, DavProtocol protocol, Func<Task>? alongside,
        CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<string?>> operation = async token =>
        {
            await using var transaction =
                await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var secret = await SwitchOnAsync(userId, protocol, token);
            if (alongside is not null) await alongside();
            await transaction.CommitAsync(token);
            return secret;
        };
        return context.Database.CreateExecutionStrategy().ExecuteAsync(operation, cancellationToken);
    }

    public async Task DisableAsync(Guid userId, DavProtocol protocol, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        Switch(row, protocol, false);
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

    private async Task<string?> SwitchOnAsync(
        Guid userId, DavProtocol protocol, CancellationToken cancellationToken)
    {
        var existing = await Track(userId, cancellationToken);
        if (existing is not null)
        {
            Switch(existing, protocol, true);
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var secret = DavSecret.Generate();
        var salt = DavSecret.NewSalt();
        var row = new DavCredential
        {
            UserId = userId,
            // Both columns posed explicitly: turning CalDAV on first must not leave a row claiming
            // an address book the account never asked for.
            CardDavEnabled = protocol is DavProtocol.CardDav,
            CalDavEnabled = protocol is DavProtocol.CalDav,
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

            Switch(winner, protocol, true);
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
    }

    private static void Switch(DavCredential row, DavProtocol protocol, bool on)
    {
        if (protocol is DavProtocol.CardDav) row.CardDavEnabled = on;
        else row.CalDavEnabled = on;
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private Task<DavCredential?> Track(Guid userId, CancellationToken cancellationToken) =>
        context.DavCredentials.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
}
