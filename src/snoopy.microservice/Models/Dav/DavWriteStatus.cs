namespace weesky.Snoopy.Microservice.Models.Dav;

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

    /// <summary>Unreadable body, or a body that is not one resource — <c>valid-address-data</c> on
    /// the book; on a calendar, the precondition the outcome carries.</summary>
    InvalidCard,

    /// <summary>A VERSION outside what the collection announces — <c>supported-address-data</c>,
    /// <c>supported-calendar-data</c>. Readable, yet refusable.</summary>
    UnsupportedVersion,

    /// <summary>A VTODO, VJOURNAL or VFREEBUSY alone — <c>supported-calendar-component</c>.</summary>
    UnsupportedComponent,

    /// <summary>Over <c>IcsGuards.MaxInstancesPerYear</c> — <c>max-instances</c>.</summary>
    TooManyInstances,

    /// <summary>The UID is held by another resource — <c>no-uid-conflict</c>, with that resource's href.</summary>
    UidConflict,

    /// <summary>Beyond <c>ContactStore.MaxCardBytes</c> or <c>IcsGuards.MaxIcsBytes</c> — <c>max-resource-size</c>.</summary>
    TooLarge,

    /// <summary>The collection is at its ceiling — <c>ContactStore.MaxPerUser</c>,
    /// <c>CalendarEventStore.MaxPerCalendar</c> — 507.</summary>
    CollectionFull,

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
