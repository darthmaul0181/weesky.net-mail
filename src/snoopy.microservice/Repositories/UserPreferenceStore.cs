using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class UserPreferenceStore(PreferencesDbContext context)
    : ScopedStore<UserPreference>(context), IUserPreferenceStore
{
    public async Task<IReadOnlyList<UserPreference>> GetAsync(Guid userId, CancellationToken cancellationToken)
        => await Scoped(p => p.UserId == userId)
            .OrderBy(p => p.PreferenceKey)
            .ToListAsync(cancellationToken);

    public Task SetAsync(Guid userId, string key, string value, CancellationToken cancellationToken)
        => UpsertByKeyAsync(
            p => p.UserId == userId && p.PreferenceKey == key,
            now => new UserPreference
            {
                UserId = userId, PreferenceKey = key, PreferenceValue = value, UpdatedAt = now
            },
            (p, now) => { p.PreferenceValue = value; p.UpdatedAt = now; },
            cancellationToken);
}
