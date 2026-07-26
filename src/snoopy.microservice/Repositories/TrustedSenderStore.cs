using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class TrustedSenderStore(PreferencesDbContext context) : ITrustedSenderStore
{
    /// <summary>
    /// What actually bounds the table. The retention sweep deletes after the fact and bounds
    /// nothing in the meantime; this refuses the row that would exceed the ceiling.
    /// </summary>
    internal const int MaxPerAccount = 1000;

    // Reaches the reader as a toast, so it speaks the screen's vocabulary — images that load —
    // rather than the table's. Interpolated, not spelled out, so the ceiling is stated once.
    internal static readonly string CapReached =
        $"You have reached the maximum of {MaxPerAccount} senders whose images always load";

    public async Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken cancellationToken)
        => await context.TrustedSenders.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Address)
            .Select(t => t.Address)
            .ToListAsync(cancellationToken);

    public async Task<Result> AddAsync(Guid userId, string address, CancellationToken cancellationToken)
    {
        var canonical = IdentityResolver.Canonical(address);
        var existing = await FindAsync(userId, canonical, cancellationToken);

        if (existing == null)
        {
            // Counted only on the branch that adds a row, so re-approving a stored address is
            // never refused by a cap it does not push against.
            var stored = await context.TrustedSenders.CountAsync(t => t.UserId == userId, cancellationToken);
            if (stored >= MaxPerAccount) return Result.Failure(CapReached);

            context.TrustedSenders.Add(new TrustedSender
            {
                UserId = userId, Address = canonical, LastUsed = DateTime.UtcNow
            });
        }
        else
        {
            existing.LastUsed = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task RemoveAsync(Guid userId, string address, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, IdentityResolver.Canonical(address), cancellationToken);
        if (row == null) return;

        context.TrustedSenders.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(Guid userId, string address, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, IdentityResolver.Canonical(address), cancellationToken);
        var now = DateTime.UtcNow;
        if (row == null || row.LastUsed.Date == now.Date) return;

        row.LastUsed = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SweepExpiredAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - retention;
        // Loaded then removed rather than ExecuteDeleteAsync: the InMemory provider the tests run
        // on never translates SQL, so a bulk-delete would be covered by nothing that could fail.
        var stale = await context.TrustedSenders
            .Where(t => t.LastUsed < cutoff)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0) return 0;

        context.TrustedSenders.RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    private async Task<TrustedSender?> FindAsync(Guid userId, string canonical, CancellationToken cancellationToken)
        => await context.TrustedSenders.FindAsync([userId, canonical], cancellationToken);
}
