using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class AppSettingStore : IAppSettingStore
{
    private readonly PreferencesDbContext _context;

    public AppSettingStore(PreferencesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<AppSetting>> GetAsync(CancellationToken cancellationToken)
        => await _context.AppSettings.AsNoTracking()
            .OrderBy(s => s.SettingKey)
            .ToListAsync(cancellationToken);

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var existing = await _context.AppSettings
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);

        if (existing is null)
        {
            _context.AppSettings.Add(new AppSetting
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

        await _context.SaveChangesAsync(cancellationToken);
    }
}
