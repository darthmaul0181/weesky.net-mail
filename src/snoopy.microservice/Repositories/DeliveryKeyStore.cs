using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class DeliveryKeyStore(PreferencesDbContext context)
    : ScopedStore<DeliveryReplyKey>(context), IDeliveryKeyStore
{
    public Task<DeliveryReplyKey?> FindAsync(CancellationToken cancellationToken)
        => Untracked.FirstOrDefaultAsync(k => k.Id == DeliveryReplyKey.SingletonId, cancellationToken);

    public async Task<bool> ReplaceAsync(byte[] keyHash, DateTime now, CancellationToken cancellationToken)
        => await LastWriterWinsAsync(async () =>
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) Set.Add(row = new DeliveryReplyKey());
            row.KeyHash = keyHash;
            row.CreatedAt = now;
            row.LastCallAt = null;
            row.UpdatedAt = now;
            await Context.SaveChangesAsync(cancellationToken);
            return DeliveryKeyWrite.Written;
        }) == DeliveryKeyWrite.Written;

    public Task<DeliveryKeyWrite> SetEnabledAsync(bool enabled, DateTime now, CancellationToken cancellationToken)
        => LastWriterWinsAsync(async () =>
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) return DeliveryKeyWrite.NoKey;
            row.Enabled = enabled;
            row.UpdatedAt = now;
            await Context.SaveChangesAsync(cancellationToken);
            return DeliveryKeyWrite.Written;
        });

    public Task<DeliveryKeyWrite> DeleteAsync(CancellationToken cancellationToken)
        => LastWriterWinsAsync(async () =>
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) return DeliveryKeyWrite.NoKey;
            Set.Remove(row);
            await Context.SaveChangesAsync(cancellationToken);
            return DeliveryKeyWrite.Written;
        });

    public async Task RecordCallAsync(DateTime calledAt, CancellationToken cancellationToken)
    {
        // Three tries, no token bump: the screen's write may land meanwhile, and neither side loses.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) return;
            row.LastCallAt = calledAt;
            try
            {
                await Context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                Context.ChangeTracker.Clear();
            }
        }
    }

    private Task<DeliveryReplyKey?> RowAsync(CancellationToken cancellationToken)
        => Set.FirstOrDefaultAsync(k => k.Id == DeliveryReplyKey.SingletonId, cancellationToken);

    private async Task<DeliveryKeyWrite> LastWriterWinsAsync(Func<Task<DeliveryKeyWrite>> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (DbUpdateException ex) when (LostRace(ex))
            {
                Context.ChangeTracker.Clear();
                if (attempt == 2) return DeliveryKeyWrite.Conflict;
            }
        }
    }

    private static bool LostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
