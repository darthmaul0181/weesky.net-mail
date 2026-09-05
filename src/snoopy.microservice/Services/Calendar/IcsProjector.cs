using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// The read half of the calendar cycle: a resource in, the columns of <c>calendar_events</c> out.
/// Pure, static and total — a resource the engine cannot expand yields a degraded projection, never
/// an exception, and never a log line: the store is the layer that has a logger.
/// </summary>
internal static class IcsProjector
{
    /// <summary>Décision 1: what an endless rule writes in <c>last_occurrence</c>, so the window
    /// query stays one range scan instead of a rule evaluation per row.</summary>
    internal static readonly DateTime NoEnd = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The DDL widths this projection has to fit: summary/location and an attendee's name are
    // VARCHAR(255), status/transparency/class VARCHAR(16), an attendee's role and partstat
    // VARCHAR(32). A value over them is cut here rather than refused: none of them is an identity.
    private const int MaxTextLength = 255;
    private const int MaxCodeLength = 16;
    private const int MaxRoleLength = 32;
    private const int MaxDescriptionBytes = 65_535;
    private const int MaxOccurrences = 50_000;
    private const string Opaque = "OPAQUE";

    // Every instant this class produces carries Kind Utc, this one included: a date nobody wrote.
    private static readonly DateTime NoInstant = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    private static readonly EventProjection Empty = new(
        string.Empty, null, null, null, NoInstant, NoInstant, false, null, false, NoInstant, NoInstant,
        null, Opaque, null, []);

    internal static EventProjection Project(IcsCalendar parsed, string calendarTimeZone)
    {
        var zone = IcsTimeZones.ResolveIana(calendarTimeZone) ?? IcsTimeZones.Utc;
        var components = IcsDocument.Components(parsed).ToList();
        // Décision 5: the columns are read on the master, or on the first exception when a client
        // sends "this occurrence only" without one.
        var master = components.FirstOrDefault(e => e.RecurrenceIdentifier is null) ?? components.FirstOrDefault();
        if (master is null) return Empty;

        var start = IcsTimeZones.Place(master.DtStart, zone, parsed);
        var end = IcsTimeZones.Place(IcsDocument.EndOf(master), zone, parsed);
        var recurring = master.RecurrenceIdentifier is null
                        && (master.RecurrenceRule is not null || master.RecurrenceDates?.GetAllDates().Any() == true);
        var (first, last) = Occurrences(parsed, components, master, recurring, start, end, zone);

        return new EventProjection(
            master.Uid ?? string.Empty,
            Text(master.Summary),
            Text(master.Location),
            Bytes(master.Description),
            start.Utc,
            end.Utc,
            master.DtStart is { HasTime: false },
            start.Zone,
            recurring,
            first,
            last,
            Upper(master.Status, MaxCodeLength),
            Upper(master.Transparency, MaxCodeLength) ?? Opaque,
            Upper(master.Class, MaxCodeLength),
            Attendees(components),
            start.Unknown);
    }

    private static (DateTime First, DateTime Last) Occurrences(
        IcsCalendar parsed, List<CalendarEvent> components, CalendarEvent master,
        bool recurring, IcsTimeZones.Placed start, IcsTimeZones.Placed end, string zone)
    {
        // The instants above are right whatever happens — the file's own VTIMEZONE answered them —
        // but the series is not always walkable, and a recurring event then takes the sentinel:
        // over-reporting keeps it in the window query, under-reporting would make it vanish after
        // its first instance. The first instance is computed either way (décision 1): an RDATE or a
        // moved override before DTSTART is the event's real start, degraded path or not.
        var earliest = Earliest(parsed, components, master, start, zone);
        if (!IcsGuards.IsWalkable(parsed) || !components.All(IcsTimeZones.Expandable))
            return (earliest, recurring ? NoEnd : end.Utc);

        if (recurring && master.RecurrenceRule is { Until: null, Count: null or 0 })
            return (earliest, NoEnd);

        var first = DateTime.MaxValue;
        var last = DateTime.MinValue;
        var seen = 0;
        try
        {
            foreach (var occurrence in parsed.GetOccurrences().TakeWhileBefore(new CalDateTime(NoEnd, IcsTimeZones.Utc)).Take(MaxOccurrences))
            {
                seen++;
                var at = IcsTimeZones.Place(occurrence.Period.StartTime, zone, parsed).Utc;
                var until = IcsTimeZones.Place(occurrence.Period.EffectiveEndTime, zone, parsed).Utc;
                if (at < first) first = at;
                if (until > last) last = until;
            }
        }
        catch (Exception)
        {
            // A rule the library refuses to evaluate — a malformed UNTIL, a BY* combination it does
            // not model. The projection degrades exactly as an unwalkable one does.
            return (earliest, recurring ? NoEnd : end.Utc);
        }

        if (seen == 0) return (earliest, end.Utc);
        return (Min(first, earliest), seen == MaxOccurrences ? NoEnd : last);
    }

    /// <summary>The first instant the series actually holds — DTSTART, unless an RDATE or a moved
    /// override sits before it. What an endless rule answers without being walked.</summary>
    private static DateTime Earliest(IcsCalendar parsed, List<CalendarEvent> components, CalendarEvent master, IcsTimeZones.Placed start, string zone)
    {
        var earliest = start.Utc;
        foreach (var date in master.RecurrenceDates?.GetAllDates() ?? [])
            earliest = Min(earliest, IcsTimeZones.Place(date, zone, parsed).Utc);
        foreach (var component in components.Where(c => c.RecurrenceIdentifier is not null))
            earliest = Min(earliest, IcsTimeZones.Place(component.DtStart, zone, parsed).Utc);
        return earliest;
    }

    private static List<AttendeeProjection> Attendees(List<CalendarEvent> components)
    {
        var attendees = new List<AttendeeProjection>();
        foreach (var component in components)
        {
            var recurrenceId = IcsDocument.InstanceIdOf(component) is { Length: > 0 } id ? id : null;
            if (component.Organizer is { } organizer && Address(organizer.Value) is { } email)
                attendees.Add(new AttendeeProjection(recurrenceId, email, Text(organizer.CommonName), null, null, true));
            foreach (var attendee in component.Attendees ?? [])
                if (attendee is not null && Address(attendee.Value) is { } address)
                    attendees.Add(new AttendeeProjection(
                        recurrenceId, address, Text(attendee.CommonName),
                        Upper(attendee.Role, MaxRoleLength), Upper(attendee.ParticipationStatus, MaxRoleLength), false));
        }

        return attendees;
    }

    /// <summary>The one reading of an ATTENDEE or ORGANIZER value, shared with
    /// <see cref="IcsGuards"/> so the gate and the projection judge the same address.</summary>
    internal static string? Address(Uri? value) => value switch
    {
        null => null,
        { IsAbsoluteUri: true } when value.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase) =>
            Trimmed(value.AbsoluteUri["mailto:".Length..]),
        _ => Trimmed(value.OriginalString),
    };

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    private static string? Upper(string? value, int max) => Cut(Trimmed(value)?.ToUpperInvariant(), max);

    private static string? Text(string? value) => Cut(Trimmed(value), MaxTextLength);

    private static string? Cut(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max];

    // The column is a TEXT: its ceiling is in bytes, and cutting mid-character would store a
    // sequence no reader can decode.
    private static string? Bytes(string? value)
    {
        if (Trimmed(value) is not { } text) return null;
        if (Encoding.UTF8.GetByteCount(text) <= MaxDescriptionBytes) return text;

        var chars = 0;
        var used = 0;
        while (chars < text.Length)
        {
            var width = char.IsHighSurrogate(text[chars]) && chars + 1 < text.Length ? 2 : 1;
            var next = Encoding.UTF8.GetByteCount(text.AsSpan(chars, width));
            if (used + next > MaxDescriptionBytes) break;
            used += next;
            chars += width;
        }

        return text[..chars];
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
