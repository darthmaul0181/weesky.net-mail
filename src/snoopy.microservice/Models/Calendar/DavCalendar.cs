namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>One calendar as the protocol serves it: the columns its properties read, no more.</summary>
public sealed record DavCalendar(Guid Id, Guid UserId, string DavName, string DisplayName,
    string Description, string Color, int Order, string TimeZone);
