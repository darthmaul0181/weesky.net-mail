namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>One calendar as the protocol serves it: the columns its properties read, no more.
/// <see cref="IsReadOnly"/> is true on the birthdays calendar alone (spec § 6).</summary>
public sealed record DavCalendar(Guid Id, Guid UserId, string DavName, string DisplayName,
    string Description, string Color, int Order, string TimeZone, bool IsReadOnly = false);
