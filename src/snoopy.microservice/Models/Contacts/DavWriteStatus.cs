namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// How a DAV write ended. The refusals are not interchangeable: each maps to its own named
/// precondition, and a client acts differently on each — an <see cref="InvalidCard"/> is
/// abandoned, an <see cref="UnsupportedVersion"/> may be re-exported, a <see cref="UidConflict"/>
/// sends the client to re-read the conflicting href.
/// </summary>
public enum DavWriteStatus
{
    Created,
    Replaced,
    Deleted,

    /// <summary>Unreadable body, or a body carrying more than one card — <c>valid-address-data</c>.</summary>
    InvalidCard,

    /// <summary>A VERSION outside 3.0/4.0 — <c>supported-address-data</c>. Readable, yet refusable.</summary>
    UnsupportedVersion,

    /// <summary>The UID is held by another resource — <c>no-uid-conflict</c>, with that resource's href.</summary>
    UidConflict,

    /// <summary>Beyond <c>ContactStore.MaxCardBytes</c> — <c>max-resource-size</c>.</summary>
    TooLarge,

    /// <summary>The book is at <c>ContactStore.MaxPerUser</c> — 507.</summary>
    BookFull,

    /// <summary>
    /// A create-only PUT found the name already holding a visible resource — the creation race's
    /// loser, refused inside the gate with nothing written: the edge answers 412 and may archive
    /// the body, which genuinely never reached the book.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The If-Match the request carried no longer holds under the state lock — a replacement
    /// committed between the edge's pre-check and the gate. The replacement race's loser, refused
    /// with nothing written: 412, and a PUT body may be archived, it genuinely never landed.
    /// </summary>
    PreconditionFailed,

    NotFound,

    /// <summary>A lock wait or deadlock the store could not resolve — retry later, never a 500.</summary>
    Busy
}
