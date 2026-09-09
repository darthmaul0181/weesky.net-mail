using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using IcsCalendar = Ical.Net.Calendar;
using static weesky.Snoopy.Microservice.Services.Dav.DavPropertyTables;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The closed property set the server serves on the calendar tree, one table per
/// <see cref="DavResourceKind"/> — the twin of <c>CardDavProperties</c>, and written once here
/// rather than discovered slice by slice through bug reports.
/// </summary>
internal static class CalDavProperties
{
    private const string HomeDisplayName = "Calendars";

    /// <summary>The two RFC 4791 § 7.5.1 makes mandatory of a calendar collection, and no more.</summary>
    private static readonly string[] Collations = [DavCollation.AsciiCasemap, DavCollation.Octet];

    /// <summary>What supported-calendar-data announces, and therefore what a report body may ask
    /// for: CalendarDataRequest reads these rather than spelling them a second time.</summary>
    internal const string CalendarDataMediaType = "text/calendar";
    internal const string CalendarDataVersion = "2.0";

    private static readonly Dictionary<DavResourceKind, PropertySet> Tables = new()
    {
        [DavResourceKind.CalendarCollection] = IntermediateCollection("Calendar Homes"),

        [DavResourceKind.CalendarHome] = Set(
            (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
                new XElement(DavXml.Dav + "collection"))),
            (DavXml.Dav + "displayname", _ => new XElement(DavXml.Dav + "displayname", HomeDisplayName)),
            // The home's Allow names REPORT and expand-property is what it serves, so the property
            // has to say so rather than come back in a 404 propstat.
            (DavXml.Dav + "supported-report-set", _ => ReportSet(DavXml.Dav + "expand-property")),
            (DavXml.Dav + "current-user-principal", CurrentUserPrincipal)),

        [DavResourceKind.Event] = Excluding(
            Set(
                (DavXml.Dav + "getetag", r => FromEvent(r, DavXml.Dav + "getetag", EntityTag)),
                (DavXml.Dav + "getcontenttype", r => FromEvent(r, DavXml.Dav + "getcontenttype",
                    _ => DavHeaders.CalendarContentType)),
                // UTF-8 BYTES, never characters: a client that cuts at the announced length
                // receives a truncated file, invalid, with nothing to say why.
                (DavXml.Dav + "getcontentlength", r => FromEvent(r, DavXml.Dav + "getcontentlength",
                    e => Encoding.UTF8.GetByteCount(e.IcsRaw).ToString(CultureInfo.InvariantCulture))),
                (DavXml.Dav + "getlastmodified", r => FromEvent(r, DavXml.Dav + "getlastmodified",
                    e => HttpDate(e.UpdatedAt))),
                (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype")),
                (DavXml.Dav + "current-user-privilege-set", _ => PrivilegeSet()),
                // Both, because REPORT answers both on one resource: RFC 4791 § 7.8 and § 7.9 each
                // define their report on a calendar object resource as much as on a collection.
                (DavXml.Dav + "supported-report-set", _ => ReportSet(
                    DavXml.CalDav + "calendar-multiget", DavXml.CalDav + "calendar-query")),
                (DavXml.CalDav + "calendar-data", r => FromEvent(r, DavXml.CalDav + "calendar-data",
                    e => e.IcsRaw))),
            DavXml.CalDav + "calendar-data"),
    };

    /// <summary>
    /// The calendar's table for one year. Only <c>calendar-timezone</c> depends on the clock, and
    /// it depends on nothing finer than the year, so one table is built per year and shared rather
    /// than eighteen factories being rebuilt for every calendar of every PROPFIND.
    /// </summary>
    private static readonly ConcurrentDictionary<int, PropertySet> CalendarTables = new();

    /// <summary>
    /// Any valid year works below: only <see cref="CalendarPropertyNames"/>' key set is ever read
    /// off the table it names, never a value, and <c>Set</c> stores factories without invoking any
    /// of them, so today the <c>calendar-timezone</c> factory's <c>year</c> closure never runs.
    /// That is exactly why the year still has to be one <see cref="DateTime"/> accepts: hoisting a
    /// year-derived value out of a factory — the most natural cleanup imaginable — would make an
    /// invalid year throw inside THIS static initialiser, a <see cref="TypeInitializationException"/>
    /// on first touch that 500s the whole DAV surface at startup, both protocols, from a stack
    /// trace nowhere near the edit that caused it.
    /// </summary>
    private const int AnyValidYear = 2000;

    /// <summary>
    /// The calendar's own closed set of NAMES — the one source of what a calendar always carries,
    /// read by <see cref="CalendarPropertyUpdate"/> to judge a <c>DAV:remove</c> so that set can
    /// never drift from what this table actually serves. Year-independent: the year only varies
    /// <c>calendar-timezone</c>'s VALUE, never whether the table names it, so any year's table
    /// names the same set.
    /// </summary>
    internal static readonly IReadOnlyList<XName> CalendarPropertyNames = CalendarTable(AnyValidYear).Names;

    /// <summary>
    /// The calendar tree's own shapes, and above them the principal's: an expand-property on a
    /// calendar nests into <c>owner</c> and <c>current-user-principal</c>, so the tables it resolves
    /// against must reach the shapes those hrefs designate.
    /// The clock is the request's, never <see cref="DateTime.UtcNow"/>: a table reading the system
    /// clock gives an assertion that depends on the day it runs.
    /// </summary>
    internal static (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource, TimeProvider clock)
    {
        if (resource.Kind is DavResourceKind.Calendar)
        {
            var table = CalendarTables.GetOrAdd(clock.GetUtcNow().Year, CalendarTable);
            return DavPropertyTables.Resolve(request, table, resource);
        }

        return Tables.TryGetValue(resource.Kind, out var set)
            ? DavPropertyTables.Resolve(request, set, resource)
            : DavPrincipalProperties.Resolve(request, resource);
    }

    /// <summary>The quoted entity tag, shared with the <c>ETag</c> header a GET answers.</summary>
    internal static string EntityTag(DavEvent member) => DavPropertyTables.EntityTag(member.IcsHash);

    private static PropertySet CalendarTable(int year) => Set(
        (DavXml.Dav + "resourcetype", _ => new XElement(DavXml.Dav + "resourcetype",
            new XElement(DavXml.Dav + "collection"), new XElement(DavXml.CalDav + "calendar"))),
        (DavXml.Dav + "displayname", r => FromCalendar(r, DavXml.Dav + "displayname",
            c => c.DisplayName)),
        (DavXml.CalDav + "calendar-description", r => FromCalendar(r,
            DavXml.CalDav + "calendar-description", c => c.Description)),
        (DavXml.Apple + "calendar-color", r => FromCalendar(r, DavXml.Apple + "calendar-color",
            c => c.Color)),
        (DavXml.Apple + "calendar-order", r => FromCalendar(r, DavXml.Apple + "calendar-order",
            c => c.Order.ToString(CultureInfo.InvariantCulture))),
        // A zone the tzdb no longer holds answers 404 rather than throwing: Emit hands the raw id
        // to Ical.Net, which refuses it, and allprop pours this property — one hand-edited row would
        // 500 the whole Depth: 1 of the home. IcsComposer guards its own call for the same reason.
        (DavXml.CalDav + "calendar-timezone", r => r.Calendar is { } c
            && IcsTimeZones.IsKnownIana(c.TimeZone)
                ? new XElement(DavXml.CalDav + "calendar-timezone", TimeZoneDocument(c.TimeZone, year))
                : null),
        (DavXml.CalDav + "supported-calendar-component-set", _ => new XElement(
            DavXml.CalDav + "supported-calendar-component-set",
            new XElement(DavXml.CalDav + "comp", new XAttribute("name", "VEVENT")))),
        (DavXml.CalDav + "supported-calendar-data", _ => new XElement(
            DavXml.CalDav + "supported-calendar-data",
            new XElement(DavXml.CalDav + "calendar-data",
                new XAttribute("content-type", CalendarDataMediaType),
                new XAttribute("version", CalendarDataVersion)))),
        (DavXml.CalDav + "supported-collation-set", _ => new XElement(
            DavXml.CalDav + "supported-collation-set",
            Collations.Select(c => new XElement(DavXml.CalDav + "supported-collation", c)))),
        // The store's own constants, never a literal recopied here: an announced value the store
        // would violate is paid for in files refused without the client understanding why.
        (DavXml.CalDav + "max-resource-size", _ => new XElement(DavXml.CalDav + "max-resource-size",
            IcsGuards.MaxIcsBytes.ToString(CultureInfo.InvariantCulture))),
        (DavXml.CalDav + "max-instances", _ => new XElement(DavXml.CalDav + "max-instances",
            IcsGuards.MaxInstancesPerYear.ToString(CultureInfo.InvariantCulture))),
        (DavXml.CalendarServer + "getctag", r => new XElement(DavXml.CalendarServer + "getctag",
            DavSyncToken.Ctag(r.State))),
        (DavXml.Dav + "sync-token",
            r => new XElement(DavXml.Dav + "sync-token", DavSyncToken.Token(r.State))),
        // What this build ANSWERS from the client's side of the wire: the division of the work into
        // tasks is not a state a client ever sees, and a report announced then refused is what made
        // DAVx5 loop on the address book.
        (DavXml.Dav + "supported-report-set", _ => ReportSet(
            DavXml.CalDav + "calendar-multiget", DavXml.CalDav + "calendar-query",
            DavXml.CalDav + "free-busy-query", DavXml.Dav + "sync-collection",
            DavXml.Dav + "expand-property")),
        (DavXml.Dav + "current-user-privilege-set", _ => PrivilegeSet()),
        (DavXml.Dav + "owner", r => Href(DavXml.Dav + "owner", DavPaths.Principal(r.UserId))),
        (DavXml.Dav + "current-user-principal", CurrentUserPrincipal));

    /// <summary>
    /// A VCALENDAR carrying the collection's zone and nothing else. The lower bound is the first of
    /// January a year back, which is what makes the block cover the transitions a client already
    /// holds files for.
    /// </summary>
    private static string TimeZoneDocument(string ianaId, int year)
    {
        var calendar = new IcsCalendar();
        calendar.AddTimeZone(
            IcsTimeZones.Emit(ianaId, new DateTime(year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        return IcsDocument.Serialize(calendar);
    }

    private static XElement? FromCalendar(
        DavResourceContext resource, XName name, Func<DavCalendar, string> value) =>
        resource.Calendar is { } calendar ? new XElement(name, value(calendar)) : null;

    private static XElement? FromEvent(
        DavResourceContext resource, XName name, Func<DavEvent, string> value) =>
        resource.Event is { } member ? new XElement(name, value(member)) : null;
}
