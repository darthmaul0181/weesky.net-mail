using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

internal interface IDavContactReader
{
    /// Every visible card of the book, streamed rather than listed: a full book with address-data
    /// runs to gigabytes, and the writer emits one response at a time.
    IAsyncEnumerable<DavCard> StreamAsync(Guid userId, CancellationToken cancellationToken);

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
}
