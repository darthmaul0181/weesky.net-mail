namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// What the sidebar's editor sends. A null field is one the caller did not touch: the colour and
/// the rank keep the values creation gave them.
/// </summary>
/// <param name="DisplayName">the label the sidebar and every client shows</param>
/// <param name="Description">null leaves it untouched</param>
/// <param name="Color">null leaves it untouched; #RRGGBB, or Apple's #RRGGBBAA</param>
/// <param name="Order">null leaves it untouched</param>
/// <param name="TimeZone">
/// A DAV client's <c>calendar-timezone</c>, already resolved to an IANA id by its reader; null from
/// the webmail, which sends the browser's zone alongside instead. Null means the fallback zone at
/// creation and "unchanged" on an update.
/// </param>
public sealed record CalendarWrite(
    string DisplayName, string? Description, string? Color, int? Order, string? TimeZone = null);
