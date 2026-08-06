using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class AppSettingStore(PreferencesDbContext context)
    : ScopedStore<AppSetting>(context), IAppSettingStore
{
    // Unscoped on purpose: an application setting belongs to no user.
    public async Task<IReadOnlyList<AppSetting>> GetAsync(CancellationToken cancellationToken)
        => await Untracked
            .OrderBy(s => s.SettingKey)
            .ToListAsync(cancellationToken);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        => UpsertByKeyAsync(
            s => s.SettingKey == key,
            now => new AppSetting { SettingKey = key, SettingValue = value, UpdatedAt = now },
            (s, now) => { s.SettingValue = value; s.UpdatedAt = now; },
            cancellationToken);
}
