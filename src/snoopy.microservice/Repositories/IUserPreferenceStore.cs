using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Reads and writes the rows an account has set. Knows nothing about which keys exist or what
/// they may hold — that is the registry's business, and the caller validates before writing.
/// </summary>
public interface IUserPreferenceStore
{
    Task<IReadOnlyList<UserPreference>> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sets or corrects one preference. The pair (account, key) is the row.</summary>
    Task SetAsync(Guid userId, string key, string value, CancellationToken cancellationToken);
}
