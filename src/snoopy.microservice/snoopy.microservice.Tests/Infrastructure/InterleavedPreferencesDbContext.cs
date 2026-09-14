namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// Runs another writer's work just before each of its first <paramref name="races"/> saves reaches
/// the database, so the save meets a row changed or removed after it was read — the interleaving a
/// concurrency token exists to catch, which the InMemory provider then reports for real.
/// </summary>
internal sealed class InterleavedPreferencesDbContext(string databaseName, Func<Task> otherWriter, int races = 1)
    : PreferencesTestDbContext(databaseName)
{
    private int raced;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (raced++ < races) await otherWriter();
        return await base.SaveChangesAsync(cancellationToken);
    }
}
