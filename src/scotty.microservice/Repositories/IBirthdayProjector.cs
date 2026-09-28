using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

/// <summary>Keeps the birthdays calendar on the contacts (spec, décision 4). Both methods run inside
/// the caller's transaction and before its SaveChanges.</summary>
public interface IBirthdayProjector
{
    /// <summary>The events of every relevant contact change the context is tracking.</summary>
    Task ProjectTrackedAsync(CancellationToken cancellationToken);

    /// <summary>The whole calendar aligned on its owner's contacts.</summary>
    Task RebuildAsync(Calendar calendar, CancellationToken cancellationToken);
}
