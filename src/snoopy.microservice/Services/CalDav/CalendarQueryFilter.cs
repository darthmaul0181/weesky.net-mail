using System.Globalization;
using System.Xml.Linq;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The calendar-query filter (RFC 4791 § 9.7), parsed strictly and evaluated on the file's own
/// object model — never on the columns, which hold the master alone. The rule of 4c decision 11,
/// word for word: a filter we cannot evaluate answers <c>403 supported-filter</c>, never a result
/// that pretends; one that is malformed — a bound-less or backwards <c>time-range</c>, a date in
/// another form — answers <c>403 valid-filter</c>, so the client knows which of its body or its
/// filter is refused; an unknown collation alone answers <c>supported-collation</c>.
/// </summary>
internal static class CalendarQueryFilter
{
    private static readonly XName CompFilterName = DavXml.CalDav + "comp-filter";
    private static readonly XName PropFilterName = DavXml.CalDav + "prop-filter";
    private static readonly XName ParamFilterName = DavXml.CalDav + "param-filter";
    private static readonly XName TextMatchName = DavXml.CalDav + "text-match";
    private static readonly XName IsNotDefinedName = DavXml.CalDav + "is-not-defined";
    private static readonly XName TimeRangeName = DavXml.CalDav + "time-range";

    private const string VCalendar = "VCALENDAR";
    private const string VEvent = "VEVENT";
    private const string VAlarm = "VALARM";
    private const string UtcFormat = "yyyyMMdd'T'HHmmss'Z'";

    private static readonly CalendarQuerySpec WholeCalendar = new(true, false, null, [], []);
    private static readonly CalendarQuerySpec Nothing = new(false, true, null, [], []);

    /// <summary>Throws <see cref="DavPreconditionException"/> (<c>supported-filter</c>,
    /// <c>supported-collation</c> or <c>valid-filter</c>) on the forms § 8 refuses. Open bounds
    /// are closed here, at <see cref="OccurrenceExpander.MaxYears"/> of the other.</summary>
    internal static CalendarQuerySpec Parse(XElement filter)
    {
        RefuseAnyOf(filter);
        if (filter.Elements().ToList() is not [{ } root] || root.Name != CompFilterName || !Named(root, VCalendar))
            throw Malformed();

        RefuseAnyOf(root);
        var children = root.Elements().ToList();
        if (children is [{ } only] && only.Name == IsNotDefinedName) return Nothing;

        XElement? vevent = null;
        foreach (var child in children)
        {
            if (child.Name != CompFilterName || !Named(child, VEvent) || vevent is not null) throw Unsupported();
            vevent = child;
        }

        return vevent is null ? WholeCalendar : ParseEvent(vevent);
    }

    /// <summary>
    /// Strict <c>yyyyMMdd'T'HHmmss'Z'</c>. RFC 4791 § 9.9: a time-range MUST carry at least one of
    /// start/end — neither is a refusal, never a window invented around now. One missing bound is
    /// closed at <see cref="OccurrenceExpander.MaxSpan"/> of the other; <paramref name="bothRequired"/>
    /// demands the two; <c>end ≤ start</c> and a window wider than that span are refused — the cap
    /// grows with the window, so nothing else bounds what one candidate can make the walk produce.
    /// <paramref name="refusal"/> names the precondition to throw (<c>CALDAV:valid-filter</c>
    /// inside a filter), null for a plain <see cref="DavBadRequestException"/>.
    /// </summary>
    internal static TimeRangeSpec ParseTimeRange(XElement timeRange, bool bothRequired, XName? refusal)
    {
        var start = Bound(timeRange, "start", refusal);
        var end = Bound(timeRange, "end", refusal);
        if ((start is null && end is null) || (bothRequired && (start is null || end is null)))
            throw Refusal(refusal, "A time-range names no bound the report can close.");

        var from = start ?? OccurrenceExpander.Shift(end!.Value, -OccurrenceExpander.MaxSpan);
        var to = end ?? OccurrenceExpander.Shift(start!.Value, OccurrenceExpander.MaxSpan);
        if (to <= from) throw Refusal(refusal, "A time-range ends before it starts.");
        if (to - from > OccurrenceExpander.MaxSpan)
            throw Refusal(refusal, $"A time-range spans more than the {OccurrenceExpander.MaxYears} years served.");
        return new TimeRangeSpec(from, to);
    }

    /// <summary>A « date with UTC time » (RFC 4791 § 9.9), or null in any other form.</summary>
    internal static DateTime? UtcInstant(string value) =>
        DateTime.TryParseExact(value, UtcFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;

    /// <summary>
    /// The three column equalities the store may preselect on — STATUS, TRANSP and CLASS, each when
    /// its text-match is a plain <c>equals</c> without negation, spelled as the projection spells
    /// the column. Only a preselection: the file confirms every row it keeps, so a value the column
    /// could not hold whole asks nothing of it.
    /// </summary>
    internal static EventColumnFilter Columns(CalendarQuerySpec spec)
    {
        string? status = null, transparency = null, classification = null;
        foreach (var filter in spec.PropFilters)
        {
            if (filter.IsNotDefined || filter.ParamFilters.Count > 0
                || filter.TextMatches is not [{ MatchType: TextMatchKind.Equals, Negate: false } match])
                continue;
            var code = match.Value.Trim().ToUpperInvariant();
            if (code.Length is 0 or > IcsProjector.MaxCodeLength) continue;
            switch (filter.Name.ToUpperInvariant())
            {
                case "STATUS": status = code; break;
                case "TRANSP": transparency = code; break;
                case "CLASS": classification = code; break;
            }
        }

        return new EventColumnFilter(status, transparency, classification);
    }

    /// <summary>Whether one resource satisfies the filter, judged on its parsed file.</summary>
    internal static bool Matches(IcsCalendar parsed, CalendarQuerySpec spec, string calendarTimeZone)
    {
        if (spec.NoneMatch) return false;
        if (spec.TimeRange is { } range
            && !OccurrenceExpander.Overlaps(parsed, range.FromUtc, range.ToUtc, calendarTimeZone))
            return false;

        if (spec.PropFilters.Count == 0 && spec.AlarmFilters.Count == 0) return true;

        // RFC 4791 § 9.7.1: « the targeted calendar component » matches all of them — ONE component
        // of the resource, master or override, satisfies every clause together (sabre's reading).
        return IcsDocument.Components(parsed).Any(component =>
            spec.PropFilters.All(filter => MatchesPropFilter(component, filter))
            && spec.AlarmFilters.All(filter => MatchesAlarmFilter(parsed, component, filter, calendarTimeZone)));
    }

    // ---- evaluation, on the object model ------------------------------------------------------

    private static bool MatchesPropFilter(CalendarEvent component, PropFilterSpec filter)
    {
        var instances = component.Properties
            .Where(p => p.Name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (filter.IsNotDefined) return instances.Count == 0;
        if (instances.Count == 0) return false;
        foreach (var match in filter.TextMatches)
        {
            // Negation applies to « some instance matches », not to each instance in turn.
            if (instances.Any(i => Literals(i).Any(match.Satisfies)) == match.Negate) return false;
        }

        return filter.ParamFilters.All(p => MatchesParam(instances, p));
    }

    private static bool MatchesParam(List<ICalendarProperty> instances, ParamFilterSpec filter)
    {
        var named = instances.SelectMany(i => Parameters(i, filter.Name)).ToList();
        if (filter.IsNotDefined) return named.Count == 0;
        if (filter.TextMatch is not { } match) return named.Count > 0;
        return named.SelectMany(p => p.Values ?? []).OfType<string>().Any(match.Satisfies) != match.Negate;
    }

    private static bool MatchesAlarmFilter(IcsCalendar parsed, CalendarEvent component, AlarmFilterSpec filter,
        string calendarTimeZone)
    {
        var alarmed = component.Alarms.Count > 0;
        if (filter.IsNotDefined) return !alarmed;
        return filter.TimeRange is { } range
            ? alarmed && OccurrenceExpander.AlarmFires(parsed, range.FromUtc, range.ToUtc, calendarTimeZone, component)
            : alarmed;
    }

    private static IEnumerable<CalendarParameter> Parameters(ICalendarProperty property, string name) =>
        property.Parameters.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A property's values as iCalendar text — a DATE-TIME as the file spells it
    /// (<c>20260906T120000Z</c>), a participant as its address: what RFC 4791 § 9.7.5 compares.</summary>
    private static IEnumerable<string> Literals(ICalendarProperty property) =>
        (property.Values ?? []).Select(Literal).OfType<string>();

    private static string? Literal(object? value) => value switch
    {
        null => null,
        string text => text,
        CalDateTime at => IcsDocument.LiteralOf(at),
        Attendee attendee => attendee.Value?.ToString(),
        Organizer organizer => organizer.Value?.ToString(),
        _ => value.ToString(),
    };

    // ---- parsing --------------------------------------------------------------------------------

    private static CalendarQuerySpec ParseEvent(XElement vevent)
    {
        RefuseAnyOf(vevent);
        var children = vevent.Elements().ToList();
        if (children is [{ } only] && only.Name == IsNotDefinedName) return Nothing;

        TimeRangeSpec? range = null;
        var propFilters = new List<PropFilterSpec>();
        var alarmFilters = new List<AlarmFilterSpec>();
        foreach (var child in children)
        {
            if (child.Name == TimeRangeName)
            {
                if (range is not null) throw Malformed();
                range = ParseTimeRange(child, false, CalDavError.ValidFilter);
            }
            else if (child.Name == PropFilterName) propFilters.Add(ParsePropFilter(child));
            else if (child.Name == CompFilterName && Named(child, VAlarm)) alarmFilters.Add(ParseAlarmFilter(child));
            else if (child.Name == IsNotDefinedName) throw Malformed();
            else throw Unsupported();
        }

        return new CalendarQuerySpec(false, false, range, propFilters, alarmFilters);
    }

    private static AlarmFilterSpec ParseAlarmFilter(XElement element)
    {
        RefuseAnyOf(element);
        var isNotDefined = false;
        TimeRangeSpec? range = null;
        foreach (var child in element.Elements())
        {
            if (child.Name == IsNotDefinedName) isNotDefined = true;
            else if (child.Name == TimeRangeName && range is null) range = ParseTimeRange(child, false, CalDavError.ValidFilter);
            else if (child.Name == TimeRangeName) throw Malformed();
            else throw Unsupported();
        }

        // The grammar's is-not-defined stands alone (§ 9.7.1).
        if (isNotDefined && (range is not null || element.Elements().Count() > 1)) throw Malformed();
        return new AlarmFilterSpec(isNotDefined, range);
    }

    private static PropFilterSpec ParsePropFilter(XElement element)
    {
        RefuseAnyOf(element);
        var name = Attribute(element, "name");
        if (string.IsNullOrEmpty(name)) throw Malformed();
        var isNotDefined = false;
        TextMatchSpec? textMatch = null;
        var paramFilters = new List<ParamFilterSpec>();
        foreach (var child in element.Elements())
        {
            if (child.Name == IsNotDefinedName) isNotDefined = true;
            else if (child.Name == TextMatchName && textMatch is null) textMatch = ParseTextMatch(child);
            else if (child.Name == TextMatchName) throw Malformed();
            else if (child.Name == ParamFilterName) paramFilters.Add(ParseParamFilter(child));
            else throw Unsupported();   // a time-range on a property, or anything else (spec § 8)
        }

        if (isNotDefined && (textMatch is not null || paramFilters.Count > 0)) throw Malformed();
        return new PropFilterSpec(name, true, isNotDefined, textMatch is null ? [] : [textMatch], paramFilters);
    }

    private static ParamFilterSpec ParseParamFilter(XElement element)
    {
        var name = Attribute(element, "name");
        if (string.IsNullOrEmpty(name)) throw Malformed();
        var children = element.Elements().ToList();
        if (children.Count > 1) throw Malformed();
        var child = children.FirstOrDefault();
        if (child is null) return new ParamFilterSpec(name, false, null);
        if (child.Name == IsNotDefinedName) return new ParamFilterSpec(name, true, null);
        if (child.Name == TextMatchName) return new ParamFilterSpec(name, false, ParseTextMatch(child));
        throw Unsupported();
    }

    // RFC 4791's text-match has no match-type: « contains » is its one semantic, and the CardDAV
    // attribute is honoured when a client writes it — served as contains, an « equals » would hand
    // it more than it asked and it would file the surplus as matches.
    private static TextMatchSpec ParseTextMatch(XElement element)
    {
        var comparer = DavCollation.Resolve(Attribute(element, "collation"), DavCollationSet.CalDav);
        var matchType = Attribute(element, "match-type") switch
        {
            null or "contains" => TextMatchKind.Contains,
            "equals" => TextMatchKind.Equals,
            "starts-with" => TextMatchKind.StartsWith,
            "ends-with" => TextMatchKind.EndsWith,
            _ => throw Unsupported(),
        };
        var negate = Attribute(element, "negate-condition") switch
        {
            null or "no" => false,
            "yes" => true,
            _ => throw Malformed(),
        };
        return new TextMatchSpec(element.Value, matchType, negate, comparer);
    }

    private static DateTime? Bound(XElement timeRange, string name, XName? refusal)
    {
        if (Attribute(timeRange, name) is not { } value) return null;
        return UtcInstant(value) ?? throw Refusal(refusal, $"A time-range {name} is not a date with UTC time.");
    }

    /// <summary>RFC 4791 defines no <c>test</c> attribute: <c>anyof</c> — CardDAV's — is a
    /// semantic this filter cannot evaluate, while <c>allof</c> is the one it already has.</summary>
    private static void RefuseAnyOf(XElement element)
    {
        if (Attribute(element, "test") is { } test && !test.Equals("allof", StringComparison.Ordinal))
            throw Unsupported();
    }

    private static bool Named(XElement element, string component) =>
        string.Equals(Attribute(element, "name"), component, StringComparison.OrdinalIgnoreCase);

    private static string? Attribute(XElement element, string name) => DavXml.Attribute(element, name, DavXml.CalDav);

    private static Exception Refusal(XName? refusal, string reason) =>
        refusal is null ? new DavBadRequestException(reason) : new DavPreconditionException(refusal);

    private static DavPreconditionException Unsupported() => new(CalDavError.SupportedFilter);

    private static DavPreconditionException Malformed() => new(CalDavError.ValidFilter);
}
