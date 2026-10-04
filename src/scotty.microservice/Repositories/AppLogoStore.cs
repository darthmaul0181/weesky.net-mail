using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

internal sealed class AppLogoStore(PreferencesDbContext context)
    : ScopedStore<AppLogoImage>(context), IAppLogoStore
{
    public async Task<byte[]?> GetImageAsync(int size, CancellationToken cancellationToken)
        => await Untracked.Where(l => l.Size == size).Select(l => l.Image).FirstOrDefaultAsync(cancellationToken);

    public async Task<DateTime?> GetUpdatedAtAsync(CancellationToken cancellationToken)
        => await Untracked.MaxAsync(l => (DateTime?)l.UpdatedAt, cancellationToken);

    // Updated in place rather than deleted and re-added: one SaveChanges is one transaction, and
    // EF refuses to track a new row under the key of one it is deleting.
    public async Task ReplaceAsync(IReadOnlyDictionary<int, byte[]> images, CancellationToken cancellationToken)
    {
        var rows = await Set.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var (size, image) in images)
        {
            var row = rows.FirstOrDefault(r => r.Size == size);
            if (row is null) Set.Add(new AppLogoImage { Size = (short)size, Image = image, UpdatedAt = now });
            else { row.Image = image; row.UpdatedAt = now; }
        }
        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await RemoveWhereAsync(Set, _ => true, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
    }
}
