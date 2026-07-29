using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class ConnectedAccountStore(PreferencesDbContext context) : IConnectedAccountStore
{
    internal const string AlreadyConnected = "This mailbox is already connected";

    public async Task<IReadOnlyList<ConnectedAccount>> ListAsync(
        Guid userId, CancellationToken cancellationToken)
        => await context.ConnectedAccounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Email)
            .ToListAsync(cancellationToken);

    public Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken)
        => context.ConnectedAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == id, cancellationToken);

    public async Task<Result<ConnectedAccount>> CreateAsync(
        ConnectedAccount row, CancellationToken cancellationToken)
    {
        var email = IdentityResolver.Canonical(row.Email);
        // Pre-checked rather than left to the unique index: MariaDB never collides two NULLs, so
        // a duplicate local mailbox (domain_id NULL) would slip straight through it.
        if (await context.ConnectedAccounts.AnyAsync(
                a => a.UserId == row.UserId && a.DomainId == row.DomainId && a.Email == email,
                cancellationToken))
            return Result.Failure<ConnectedAccount>(AlreadyConnected);

        var now = DateTime.UtcNow;
        row.Id = Guid.NewGuid();
        row.Email = email;
        row.CreationDate = now;
        context.ConnectedAccounts.Add(row);

        // The label is left empty on purpose: the UI falls back to the address at render time,
        // so a later rename of the mailbox never leaves a stale name behind.
        context.SendingIdentities.Add(new SendingIdentity
        {
            UserId = row.UserId,
            AccountId = row.Id.ToString(),
            Address = email,
            DisplayName = string.Empty,
            IsDefault = true,
            UpdatedAt = now
        });

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(row);
    }

    public async Task UpdateCipherAsync(
        ConnectedAccount row, byte[] cipher, CancellationToken cancellationToken)
    {
        context.ConnectedAccounts.Attach(row);
        row.Cipher = cipher;
        context.Entry(row).Property(a => a.Cipher).IsModified = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceCiphersAsync(
        Guid userId, IReadOnlyDictionary<Guid, byte[]> ciphers, CancellationToken cancellationToken)
    {
        var rows = await context.ConnectedAccounts
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            if (ciphers.TryGetValue(row.Id, out var cipher))
                row.Cipher = cipher;

        // A single SaveChanges: a re-key that committed by halves would strand the accounts
        // whose cipher still hangs off the previous key.
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var row = await context.ConnectedAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == id, cancellationToken);
        if (row == null) return;

        // No FK carries account_id — the '' sentinel forbids one — so the cascade is ours.
        var accountId = id.ToString();
        context.SendingIdentities.RemoveRange(await context.SendingIdentities
            .Where(i => i.UserId == userId && i.AccountId == accountId)
            .ToListAsync(cancellationToken));
        context.FolderRoleOverrides.RemoveRange(await context.FolderRoleOverrides
            .Where(o => o.UserId == userId && o.AccountId == accountId)
            .ToListAsync(cancellationToken));
        context.ConnectedAccounts.Remove(row);

        await context.SaveChangesAsync(cancellationToken);
    }
}
