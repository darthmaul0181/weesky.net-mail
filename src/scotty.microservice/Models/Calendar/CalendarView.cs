namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>
/// One calendar as the sidebar reads it. <c>IsDefault</c> is derived, never stored: the collection
/// whose <c>dav_name</c> is <c>default</c> is the one no deletion may take. <c>BirthdayReminder</c>
/// is null, and omitted from the JSON, for any calendar whose <c>Kind</c> is not <c>birthdays</c>.
/// </summary>
public sealed record CalendarView(
    Guid Id, string DavName, string DisplayName, string Description, string Color, int Order,
    string TimeZone, bool IsVisible, bool IsDefault, string Kind, string? BirthdayReminder);
