using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.WhatsNew;

namespace weesky.Scotty.Microservice.Repositories;

internal sealed class WhatsNewStore(PreferencesDbContext context)
    : ScopedStore<WhatsNewItem>(context), IWhatsNewStore
{
    public async Task<WhatsNewState> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var baseline = await Context.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId && p.PreferenceKey == UserPreferences.WhatsNewBaseline)
            .Select(p => p.PreferenceValue)
            .FirstOrDefaultAsync(cancellationToken);
        var items = await Scoped(i => i.UserId == userId)
            .OrderBy(i => i.ItemId)
            .ToListAsync(cancellationToken);

        return new WhatsNewState(baseline, items);
    }

    public async Task MarkAsync(
        Guid userId, IReadOnlyCollection<string> itemIds, WhatsNewMark mark, CancellationToken cancellationToken)
    {
        try
        {
            await StampAsync(userId, itemIds, mark, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another tab created one of these rows first; the retry finds it and only fills gaps.
            Context.ChangeTracker.Clear();
            await StampAsync(userId, itemIds, mark, cancellationToken);
        }
    }

    public async Task<bool> SetBaselineAsync(Guid userId, string version, CancellationToken cancellationToken)
    {
        if (await Context.UserPreferences.AnyAsync(
                p => p.UserId == userId && p.PreferenceKey == UserPreferences.WhatsNewBaseline, cancellationToken))
            return false;

        Context.UserPreferences.Add(new UserPreference
        {
            UserId = userId, PreferenceKey = UserPreferences.WhatsNewBaseline, PreferenceValue = version, UpdatedAt = DateTime.UtcNow
        });
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another tab wrote it between the check and the insert: theirs stands.
            Context.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task StampAsync(
        Guid userId, IReadOnlyCollection<string> itemIds, WhatsNewMark mark, CancellationToken cancellationToken)
    {
        var rows = await Set
            .Where(i => i.UserId == userId && itemIds.Contains(i.ItemId))
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var id in itemIds.Except(rows.Select(r => r.ItemId)))
        {
            var row = new WhatsNewItem { UserId = userId, ItemId = id };
            Set.Add(row);
            rows.Add(row);
        }
        foreach (var row in rows) row.Stamp(mark, now);

        await Context.SaveChangesAsync(cancellationToken);
    }
}
