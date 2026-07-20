using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class UserPreferenceStore : IUserPreferenceStore
{
    private readonly PreferencesDbContext _context;

    public UserPreferenceStore(PreferencesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<UserPreference>> GetAsync(string accountId, CancellationToken cancellationToken)
        => await _context.UserPreferences.AsNoTracking()
            .Where(p => p.AccountId == accountId)
            .OrderBy(p => p.PreferenceKey)
            .ToListAsync(cancellationToken);

    public async Task SetAsync(string accountId, string key, string value, CancellationToken cancellationToken)
    {
        var existing = await _context.UserPreferences
            .FirstOrDefaultAsync(p => p.AccountId == accountId && p.PreferenceKey == key, cancellationToken);

        if (existing is null)
        {
            _context.UserPreferences.Add(new UserPreference
            {
                AccountId = accountId,
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

        await _context.SaveChangesAsync(cancellationToken);
    }
}
