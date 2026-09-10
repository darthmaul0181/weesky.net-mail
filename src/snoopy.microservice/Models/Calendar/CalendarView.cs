namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// One calendar as the sidebar reads it. <c>IsDefault</c> is derived, never stored: the collection
/// whose <c>dav_name</c> is <c>default</c> is the one no deletion may take.
/// </summary>
public sealed record CalendarView(
    Guid Id, string DavName, string DisplayName, string Description, string Color, int Order,
    string TimeZone, bool IsVisible, bool IsDefault);
