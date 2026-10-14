using weesky.Scotty.Microservice.Models.WhatsNew;

namespace weesky.Scotty.Microservice.Repositories;

/// <summary>
/// Per-account state of the web app's novelties. Ids are opaque here: the caller validates them,
/// and the catalogue lives in the web app.
/// </summary>
public interface IWhatsNewStore
{
    Task<WhatsNewState> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Stamps the mark on every id where it is still empty, creating missing rows.</summary>
    Task MarkAsync(Guid userId, IReadOnlyCollection<string> itemIds, WhatsNewMark mark, CancellationToken cancellationToken);

    /// <summary>Writes the baseline if the account has none. False when it already had one.</summary>
    Task<bool> SetBaselineAsync(Guid userId, string version, CancellationToken cancellationToken);
}
