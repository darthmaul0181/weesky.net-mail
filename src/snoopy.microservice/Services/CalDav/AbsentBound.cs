namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>What a time-range does with a bound the client did not write. RFC 4791 § 9.9 reads it
/// as an infinity; only the callers that cannot walk one close or refuse it.</summary>
internal enum AbsentBound
{
    /// <summary>Closed at <see cref="Calendar.OccurrenceExpander.MaxSpan"/> of the other — the
    /// VALARM filter, whose walk reads a whole window rather than stopping at a first hit.</summary>
    Closed,

    /// <summary>Left absent — the VEVENT filter of a calendar-query, what DAVx5 and iOS send.</summary>
    Open,

    /// <summary>Refused — free-busy-query, which composes a VFREEBUSY between two instants.</summary>
    Refused,
}
