using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// What a <c>CALDAV:calendar-data</c> element in a report body asks for (RFC 4791 § 9.6): the
/// file as stored, or its instances over <c>[ExpandFrom, ExpandTo[</c> (§ 9.6.5). A <c>comp</c> or
/// <c>prop</c> child — § 9.6.1's partial retrieval — is ignored and the whole resource served:
/// none of the clients aimed at asks for it, and more than asked is not an error to a client.
/// </summary>
internal sealed record CalendarDataRequest(DateTime? ExpandFrom, DateTime? ExpandTo)
{
    internal static readonly XName Name = DavXml.CalDav + "calendar-data";

    private static readonly XName ExpandElement = DavXml.CalDav + "expand";
    private static readonly CalendarDataRequest AsStored = new(null, null);

    /// <summary>What supported-calendar-data announces, read from the table that announces it:
    /// what one accepts and what one advertises cannot drift apart without a test noticing.</summary>
    private static readonly string MediaType = CalDavProperties.CalendarDataMediaType;
    private static readonly string Version = CalDavProperties.CalendarDataVersion;

    internal bool Expands => ExpandFrom is not null;

    /// <summary>
    /// The <c>calendar-data</c> a report body asks for — in its <c>prop</c>, or in the
    /// <c>include</c> of an <c>allprop</c> — or null when it asks for none. May refuse, and does so
    /// before the caller has written anything: <c>supported-calendar-data</c> on a media type or a
    /// version outside what the calendar announces, <c>valid-filter</c> on an <c>expand</c> that
    /// names no window (both bounds are mandatory and UTC, § 9.6.5).
    /// </summary>
    internal static CalendarDataRequest? Asked(XDocument body)
    {
        var container = body.Root!.Element(DavXml.Prop) ?? body.Root!.Element(DavXml.Dav + "include");
        return container?.Element(Name) is { } element ? Parse(element) : null;
    }

    internal static CalendarDataRequest Parse(XElement calendarData)
    {
        if (calendarData.Attribute("content-type")?.Value is { } contentType
            && !DavHeaders.MediaTypeOf(contentType).Equals(MediaType, StringComparison.OrdinalIgnoreCase))
            throw new DavPreconditionException(CalDavError.SupportedCalendarData);
        if (calendarData.Attribute("version")?.Value is { } version && version != Version)
            throw new DavPreconditionException(CalDavError.SupportedCalendarData);

        if (calendarData.Element(ExpandElement) is not { } expand) return AsStored;
        var from = Instant(expand, "start");
        var to = Instant(expand, "end");
        if (from is null || to is null || from >= to)
            throw new DavPreconditionException(CalDavError.ValidFilter);

        // MaxIcsBytes bounds what comes IN; nothing bounded what goes out. max-instances cannot:
        // the cap grows with the window, so a daily series over two centuries stays far under it
        // and still materialises ten megabytes per member — five thousand of them per multiget.
        // The window a client may ask to expand is the one the webmail's own API already lives
        // with, and the one OccurrenceExpander.MaxYears documents the controller as enforcing.
        if (to.Value - from.Value > OccurrenceExpander.MaxSpan)
            throw new DavPreconditionException(CalDavError.ValidFilter);

        return new CalendarDataRequest(from, to);
    }

    /// <summary>The properties asked minus <c>calendar-data</c>, served by hand rather than by
    /// the event table: left in, the stored file would come out beside the expansion.</summary>
    internal static DavPropertyRequest PropertiesAsked(XDocument body)
    {
        var request = DavPropertyRequest.Parse(body);
        return request with { Names = [.. request.Names.Where(name => name != Name)] };
    }

    /// <summary>The element as a report writes it into a member's propstat. Throws
    /// <see cref="DavPreconditionException"/> (<c>max-instances</c>) on an expansion past the cap.</summary>
    internal XElement Element(DavEvent member, string timeZone) =>
        new(Name, Expands
            ? ExpandedCalendarData.Expand(member.IcsRaw, ExpandFrom!.Value, ExpandTo!.Value, timeZone)
            : member.IcsRaw);

    private static DateTime? Instant(XElement expand, string attribute) =>
        expand.Attribute(attribute)?.Value is { } value ? CalendarQueryFilter.UtcInstant(value) : null;
}
