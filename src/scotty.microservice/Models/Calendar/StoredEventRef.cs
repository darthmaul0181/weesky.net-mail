namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>One row of <c>calendar_events</c> as the invitation reader needs it: where it lives,
/// the name a rewrite must reuse, the bytes the SEQUENCE and the user's PARTSTAT are read from, and
/// who sends this event's invitations. <see cref="IcsHash"/> is what its ETag is built from, so a
/// rewrite can be made conditional on the version it was read from.</summary>
public sealed record StoredEventRef(
    Guid Id, Guid CalendarId, string DavName, string IcsRaw, string? SchedulingOwner, string IcsHash);
