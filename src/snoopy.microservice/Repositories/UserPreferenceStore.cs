using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class UserPreferenceStore(PreferencesDbContext context) : IUserPreferenceStore
{
    public async Task<IReadOnlyList<UserPreference>> GetAsync(Guid userId, CancellationToken cancellationToken)
        => await context.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.PreferenceKey)
            .ToListAsync(cancellationToken);

    public async Task SetAsync(Guid userId, string key, string value, CancellationToken cancellationToken)
    {
        var existing = await context.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key, cancellationToken);

        if (existing is null)
        {
            context.UserPreferences.Add(new UserPreference
            {
                UserId = userId,
                PreferenceKey = key,
                PreferenceValue = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.PreferenceValue = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
