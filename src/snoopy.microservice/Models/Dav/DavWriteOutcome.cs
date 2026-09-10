using weesky.Snoopy.Microservice.Services.Calendar;

namespace weesky.Snoopy.Microservice.Models.Dav;

/// <summary>
/// The result of one DAV write. <see cref="Etag"/> is null when what was stored differs from what
/// was sent — the RFC then requires NO ETag in the response, so the client re-reads; returning the
/// stored bytes' tag would be worse than none, the client believing it holds the card it sent.
/// <see cref="ConflictHref"/> is set only on <see cref="DavWriteStatus.UidConflict"/>.
/// <see cref="Sequence"/> is the rank of an accepted write, 0 on a refusal.
/// <see cref="Precondition"/> is the one the calendar gate judged an <see cref="DavWriteStatus.InvalidCard"/>
/// on, so its XML translation names it without reading the file again; null on the address book,
/// whose refusals name one element each.
/// </summary>
public sealed record DavWriteOutcome(
    DavWriteStatus Status, string? Etag, string? ConflictHref, ulong Sequence,
    IcsPrecondition? Precondition = null);
