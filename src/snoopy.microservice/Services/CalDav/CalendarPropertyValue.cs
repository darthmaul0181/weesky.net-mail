using System.Globalization;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The five properties a client writes on a calendar, and the one judge of each value —
/// <see cref="MkCalendarRequest"/> reads them at creation and <see cref="CalendarPropertyUpdate"/>
/// on a PROPPATCH, so a value the one accepts is never one the other refuses.
/// </summary>
internal static class CalendarPropertyValue
{
    internal static readonly XName DisplayName = DavXml.Dav + "displayname";
    internal static readonly XName Description = DavXml.CalDav + "calendar-description";
    internal static readonly XName Color = DavXml.Apple + "calendar-color";
    internal static readonly XName Order = DavXml.Apple + "calendar-order";
    internal static readonly XName TimeZone = DavXml.CalDav + "calendar-timezone";
    internal static readonly XName ComponentSet = DavXml.CalDav + "supported-calendar-component-set";
    internal static readonly XName ResourceType = DavXml.Dav + "resourcetype";

    /// <summary>The five, in the order an answer lists them: a document ordered by a dictionary's
    /// enumeration would differ between hosts for no reason a client could act on.</summary>
    internal static readonly XName[] Writable = [DisplayName, Description, Color, Order, TimeZone];

    /// <summary>Trimmed, and null when nothing is left: a calendar always has a name.</summary>
    internal static string? Name(string value) => value.Trim() is { Length: > 0 } name ? name : null;

    /// <summary>The rank as an integer, or null when the text is not one.</summary>
    internal static int? Rank(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order)
            ? order
            : null;

    /// <summary>
    /// The IANA id of the zone the value carries — a VCALENDAR holding exactly one VTIMEZONE whose
    /// TZID TZDB or the Windows mapping answers — or null, which every caller refuses with
    /// <c>CALDAV:valid-calendar-data</c>. More than one block names no zone: the property is
    /// singular (RFC 4791 § 5.2.2), and picking one of two would store the wrong one silently.
    /// </summary>
    internal static string? Zone(string value)
    {
        var zones = IcsDocument.TryLoad(value)?.TimeZones;
        return zones?.Count == 1 ? IcsTimeZones.ResolveIana(zones.First().TzId) : null;
    }

    /// <summary>Exactly <c>{DAV:collection, CALDAV:calendar}</c>: iCal's
    /// <c>calendarserver:subscribed</c> is a refusal, not a subscription (§ 11).</summary>
    internal static bool IsCalendar(XElement resourceType)
    {
        var declared = resourceType.Elements().Select(child => child.Name).ToHashSet();
        return declared.Count == 2
            && declared.Contains(DavXml.Dav + "collection")
            && declared.Contains(DavXml.CalDav + "calendar");
    }

    /// <summary>Every <c>comp</c> must name VEVENT: this build stores nothing else, and a
    /// collection that would refuse the client's first PUT is worse than a refused creation.</summary>
    internal static bool OnlyEvents(XElement componentSet) =>
        componentSet.Elements(DavXml.CalDav + "comp")
            .All(component => DavXml.Attribute(component, "name") == "VEVENT");

    /// <summary>The colour as the store will hold it, or null when the text is not one — read
    /// through <see cref="CalendarStore"/> because a shape written twice is two truths.</summary>
    internal static string? Colour(string value) => CalendarStore.Colour(value);
}
