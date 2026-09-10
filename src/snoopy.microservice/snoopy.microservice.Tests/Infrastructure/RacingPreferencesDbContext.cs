using Microsoft.EntityFrameworkCore;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A preferences context whose FIRST save loses a race: <paramref name="onFirstSave"/> writes the
/// winning row through a context of its own, and the save then fails exactly as the unique index
/// would in MariaDB. The InMemory provider enforces no unique index, so this is the only way a test
/// can reach a store's <see cref="DbUpdateException"/> arm at all.
/// </summary>
internal sealed class RacingPreferencesDbContext(string databaseName, Func<Task> onFirstSave)
    : PreferencesTestDbContext(databaseName)
{
    private bool raced;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (raced) return await base.SaveChangesAsync(cancellationToken);

        raced = true;
        await onFirstSave();
        throw new DbUpdateException("the unique index named another writer");
    }
}
