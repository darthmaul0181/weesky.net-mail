namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// The result of one DAV write. <see cref="Etag"/> is null when what was stored differs from what
/// was sent — the RFC then requires NO ETag in the response, so the client re-reads; returning the
/// stored bytes' tag would be worse than none, the client believing it holds the card it sent.
/// <see cref="ConflictHref"/> is set only on <see cref="DavWriteStatus.UidConflict"/>.
/// <see cref="Sequence"/> is the rank of an accepted write, 0 on a refusal.
/// </summary>
public sealed record DavWriteOutcome(
    DavWriteStatus Status, string? Etag, string? ConflictHref, ulong Sequence);
