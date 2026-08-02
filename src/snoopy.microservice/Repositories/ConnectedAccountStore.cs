using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class ConnectedAccountStore(PreferencesDbContext context)
    : ScopedStore<ConnectedAccount>(context), IConnectedAccountStore
{
    internal const string AlreadyConnected = "This mailbox is already connected";

    public async Task<IReadOnlyList<ConnectedAccount>> ListAsync(
        Guid userId, CancellationToken cancellationToken)
        => await Scoped(a => a.UserId == userId)
            .OrderBy(a => a.Email)
            .ToListAsync(cancellationToken);

    // Untracked, like every read here: UpdateCipherAsync attaches the row it gets back, which a
    // tracked copy of the same key would make impossible.
    public Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken)
        => Scoped(a => a.UserId == userId && a.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<ConnectedAccount>> CreateAsync(
        ConnectedAccount row, CancellationToken cancellationToken)
    {
        var email = IdentityResolver.Canonical(row.Email);
        // Pre-checked rather than left to the unique index: MariaDB never collides two NULLs, so
        // a duplicate local mailbox (domain_id NULL) would slip straight through it.
        if (await Set.AnyAsync(
                a => a.UserId == row.UserId && a.DomainId == row.DomainId && a.Email == email,
                cancellationToken))
            return Result.Failure<ConnectedAccount>(AlreadyConnected);

        var now = DateTime.UtcNow;
        row.Id = Guid.NewGuid();
        row.Email = email;
        row.CreationDate = now;
        Set.Add(row);

        // The label is left empty on purpose: the UI falls back to the address at render time,
        // so a later rename of the mailbox never leaves a stale name behind.
        Context.SendingIdentities.Add(new SendingIdentity
        {
            UserId = row.UserId,
            AccountId = row.Id.ToString(),
            Address = email,
            DisplayName = string.Empty,
            IsDefault = true,
            UpdatedAt = now
        });

        await Context.SaveChangesAsync(cancellationToken);

        return Result.Success(row);
    }

    public async Task UpdateCipherAsync(
        ConnectedAccount row, byte[] cipher, CancellationToken cancellationToken)
    {
        Set.Attach(row);
        row.Cipher = cipher;
        Context.Entry(row).Property(a => a.Cipher).IsModified = true;
        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceCiphersAsync(
        Guid userId, IReadOnlyDictionary<Guid, byte[]> ciphers, CancellationToken cancellationToken)
    {
        var rows = await Set
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            if (ciphers.TryGetValue(row.Id, out var cipher))
                row.Cipher = cipher;

        // A single SaveChanges: a re-key that committed by halves would strand the accounts
        // whose cipher still hangs off the previous key.
        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var row = await Set
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == id, cancellationToken);
        if (row == null) return;

        // No FK carries account_id — the '' sentinel forbids one — so the cascade is ours.
        var accountId = id.ToString();
        await RemoveWhereAsync(Context.SendingIdentities,
            i => i.UserId == userId && i.AccountId == accountId, cancellationToken);
        await RemoveWhereAsync(Context.FolderRoleOverrides,
            o => o.UserId == userId && o.AccountId == accountId, cancellationToken);
        Set.Remove(row);

        await Context.SaveChangesAsync(cancellationToken);
    }
}
