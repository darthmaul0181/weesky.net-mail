using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

public interface IDavContactReader
{
    /// Every visible card of the book up to <paramref name="upTo"/>, streamed rather than listed:
    /// a full book with address-data runs to gigabytes, and the writer emits one response at a
    /// time. The bound is the counter the caller has already read: the fallback path holds the
    /// ctag it answers as covering this very list, so a rank above it must not appear in it.
    IAsyncEnumerable<DavCard> StreamAsync(Guid userId, ulong upTo, CancellationToken cancellationToken);

    /// One card by its resource name. Null when this user does not own it, and equally when it is
    /// invisible to the protocol — the two are the same 404 to a client.
    Task<DavCard?> FindAsync(Guid userId, string davName, CancellationToken cancellationToken);

    /// The cards a multiget names, in one query rather than N. Names this user does not own simply
    /// do not come back, and the caller answers 404 inside the multistatus for each.
    Task<IReadOnlyList<DavCard>> FindManyAsync(
        Guid userId, IReadOnlyList<string> davNames, CancellationToken cancellationToken);

    /// How many visible cards the book holds — what a Depth: 1 PROPFIND announces nothing of, but
    /// what the log line of decision 18 counts.
    Task<int> CountAsync(Guid userId, CancellationToken cancellationToken);

    /// Cards whose rank is in (after, upTo], ordered by rank. The upper bound is what makes the
    /// answer honest when the rows are not read in the same transaction as the counter.
    IAsyncEnumerable<DavCard> ChangedAsync(
        Guid userId, ulong after, ulong upTo, CancellationToken cancellationToken);

    /// Tombstones in the same window, ordered by rank so a truncation can cut on a rank boundary.
    Task<IReadOnlyList<ContactTombstone>> TombstonesAsync(
        Guid userId, ulong after, ulong upTo, CancellationToken cancellationToken);
}
