using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Reads and writes instance settings. It knows nothing of which keys exist or what they
/// accept — that is the registry's job, and the caller validates before writing.
/// </summary>
public interface IAppSettingStore
{
    Task<IReadOnlyList<AppSetting>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Sets or corrects a setting. The key is the row.</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
