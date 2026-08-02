using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class AppSettingStore(PreferencesDbContext context) : IAppSettingStore
{
    public async Task<IReadOnlyList<AppSetting>> GetAsync(CancellationToken cancellationToken)
        => await context.AppSettings.AsNoTracking()
            .OrderBy(s => s.SettingKey)
            .ToListAsync(cancellationToken);

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var existing = await context.AppSettings
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);

        if (existing is null)
        {
            context.AppSettings.Add(new AppSetting
            {
                SettingKey = key,
                SettingValue = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.SettingValue = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
