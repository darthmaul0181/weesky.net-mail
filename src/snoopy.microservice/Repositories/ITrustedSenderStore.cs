using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The senders whose remote images an account loads without being asked. Addresses go in and
/// come back canonical; callers never fold them themselves.
/// </summary>
public interface ITrustedSenderStore
{
    Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Adds, or refreshes an address already stored. Fails only when the cap is reached.</summary>
    Task<Result> AddAsync(Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>Removes it. An address that is not stored is not an error.</summary>
    Task RemoveAsync(Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a stored address as still in use, at most once a day. Creates nothing: this runs
    /// for every message opened, approved sender or not.
    /// </summary>
    Task TouchAsync(Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>Drops every row untouched for longer than <paramref name="retention"/>.</summary>
    Task<int> SweepExpiredAsync(TimeSpan retention, CancellationToken cancellationToken);
}
