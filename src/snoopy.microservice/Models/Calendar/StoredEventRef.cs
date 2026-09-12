namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>One row of <c>calendar_events</c> as the invitation reader needs it: where it lives,
/// the name a rewrite must reuse, and the bytes the SEQUENCE and the user's PARTSTAT are read from.</summary>
public sealed record StoredEventRef(Guid Id, Guid CalendarId, string DavName, string IcsRaw);
