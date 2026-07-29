using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The admin-curated registry of external mail providers a user may connect a mailbox from.
/// Global, not per user: there is nothing to scope here.
/// </summary>
public interface IExternalDomainStore
{
    Task<IReadOnlyList<ExternalDomain>> ListAsync(CancellationToken cancellationToken);

    Task<ExternalDomain?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Fails when the name is already taken.</summary>
    Task<Result<ExternalDomain>> CreateAsync(ExternalDomain domain, CancellationToken cancellationToken);

    /// <summary>Rewrites every field. Fails when not found or when the name is another domain's.</summary>
    Task<Result> UpdateAsync(ExternalDomain domain, CancellationToken cancellationToken);

    /// <summary>Fails with <c>domain_in_use</c> while accounts still point at it.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
