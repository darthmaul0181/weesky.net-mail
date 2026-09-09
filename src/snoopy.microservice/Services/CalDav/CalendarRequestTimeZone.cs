using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The <c>CALDAV:timezone</c> element of a <c>calendar-query</c> (RFC 4791 § 9.8): the zone every
/// « date » and « date with local time » value of THAT request is resolved in, ahead of the
/// collection's own <c>calendar-timezone</c>. § 9.8 makes the precedence a MUST — a report that
/// reads the collection's instead answers another day's events without saying so. § 9.5 puts the
/// element in this report's grammar alone: a multiget or a free-busy-query never reads it.
/// </summary>
internal static class CalendarRequestTimeZone
{
    private static readonly XName Element = DavXml.CalDav + "timezone";

    /// <summary>The IANA id the body names, or null when it carries no element. Throws
    /// <see cref="DavPreconditionException"/> (<c>CALDAV:valid-calendar-data</c>) on a value § 9.8
    /// refuses — its grammar is « an iCalendar object with exactly one VTIMEZONE component ».</summary>
    internal static string? Of(XDocument body) =>
        body.Root?.Element(Element) is not { } element
            ? null
            : CalendarPropertyValue.Zone(element.Value)
              ?? throw new DavPreconditionException(CalDavError.ValidCalendarData);
}
