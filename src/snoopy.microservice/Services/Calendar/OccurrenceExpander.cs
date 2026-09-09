using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// One resource, one window, a flat list of instances. Pure, static and total: a series the engine
/// refuses to walk yields the master alone and a rule it cannot evaluate yields nothing — the store
/// is the layer that has a logger.
///
/// <c>RECURRENCE-ID;RANGE=THISANDFUTURE</c> (5a residual, assumed and documented rather than
/// fixed): Ical.Net 5.2.3 parses the parameter but never applies it — an override with RANGE
/// replaces the one instance its RECURRENCE-ID names, exactly as a plain override would, and
/// every later instance walks as the master describes it. sabre does not apply it either, and no
/// client this tranche targets (iOS, DAVx⁵, Thunderbird) writes it — they split the series into
/// two resources instead. A stored PUT carrying RANGE is kept verbatim and served back unchanged;
/// only the *effect* of "this and every future instance" is what neither engine computes.
/// </summary>
internal static class OccurrenceExpander
{
    /// <summary>The span an API window may cover. The controller enforces it; the walk below stays
    /// finite for any input on its own.</summary>
    internal const int MaxYears = 5;

    /// <summary>The same span as a duration, for the windows a client writes itself — an
    /// <c>expand</c>, a <c>time-range</c>: the cap grows with the window, so nothing but this
    /// bounds what one member can produce.</summary>
    internal static readonly TimeSpan MaxSpan = TimeSpan.FromDays(366d * MaxYears);

    private const string EndAnchor = "END";

    /// <summary>The walker's margin and the preselection's are the one margin: a right answer
    /// depends on their being equal, never on their looking alike.</summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    /// <summary>The repetitions one alarm may contribute to a window. Ten thousand for the day of
    /// slack the walk carries on each side, which is more than any real reminder writes.</summary>
    private const int MaxRings = 10_000;

    /// <summary>10 000 instances per year of window, the density the PUT gate admits, plus the
    /// one that proves the ceiling was reached. The walk stops there; the expanded report refuses
    /// there — the same number, so nothing can be cut by one and served whole by the other.</summary>
    internal static int CapFor(DateTime fromUtc, DateTime toUtc) =>
        IcsGuards.MaxInstancesPerYear
        * Math.Max(1, (int)Math.Ceiling((toUtc - fromUtc).TotalDays / 365.2425)) + 1;

    /// <summary>
    /// The window is <c>[fromUtc, toUtc[</c>. <paramref name="calendarTimeZone"/> poses what the
    /// file left unposed, <paramref name="viewTimeZone"/> cuts the days a floating instance falls
    /// on. All-day dates are read as dates — UTC midnights, the reading RFC 4791 § 9.9 gives a
    /// DATE value — so the same day is the same day for every reader.
    /// </summary>
    internal static IReadOnlyList<EventOccurrence> Expand(
        Guid eventId, Guid calendarId, IcsCalendar parsed, DateTime fromUtc, DateTime toUtc,
        string calendarTimeZone, string viewTimeZone) =>
        Over(eventId, calendarId, parsed, fromUtc, toUtc, calendarTimeZone, viewTimeZone).Run();

    /// <summary>Whether one instance at least overlaps the window — the very walk of
    /// <see cref="Expand"/>, stopped at the first one found (RFC 4791 § 9.9 on a VEVENT). A null
    /// bound is that side's infinity: an endless series always answers an endless question, and the
    /// walk is lazy. An absent start walks from <see cref="FloorOf"/> rather than from
    /// <see cref="DateTime.MinValue"/>, which the engine refuses to walk at all.
    /// <paramref name="component"/> narrows the instances to those one component sources — the one
    /// a filter is being judged on — and null takes them all.</summary>
    internal static bool Overlaps(IcsCalendar parsed, DateTime? fromUtc, DateTime? toUtc,
        string calendarTimeZone, CalendarEvent? component = null)
    {
        var found = Over(Guid.Empty, Guid.Empty, parsed, fromUtc ?? FloorOf(parsed),
                toUtc ?? DateTime.MaxValue, calendarTimeZone, calendarTimeZone)
            .Run(firstOnly: component is null);
        if (component is null) return found.Count > 0;

        var components = IcsDocument.Components(parsed).ToList();
        return found.Any(o => ReferenceEquals(SourceOf(o, components), component));
    }

    /// <summary>
    /// Whether an alarm of one instance fires inside <c>[fromUtc, toUtc[</c> (RFC 4791 § 9.9 on a
    /// VALARM). The instances of <c>[fromUtc − 1 day, toUtc + 1 day[</c> are walked and each
    /// trigger read as the instant it pins, or as its distance from the instance's start — or its
    /// end, which RELATED=END names — then walked through its own REPEAT/DURATION rings, if any
    /// (see <see cref="Rings"/>). A day of slack and no more: a TRIGGER:-P1W before an instance
    /// past the window is missed, an incomplete answer rather than a false one, and the bound is
    /// assumed — a week of slack would have every query reread the whole calendar.
    /// <paramref name="component"/> narrows the instances to those one component of the file
    /// sources — the one a filter is being judged on — and null takes them all.
    /// </summary>
    internal static bool AlarmFires(IcsCalendar parsed, DateTime fromUtc, DateTime toUtc, string calendarTimeZone,
        CalendarEvent? component = null)
    {
        var zone = IcsTimeZones.ResolveIana(calendarTimeZone) ?? IcsTimeZones.Utc;
        var components = IcsDocument.Components(parsed).ToList();
        var occurrences = Expand(Guid.Empty, Guid.Empty, parsed, Shift(fromUtc, -Margin), Shift(toUtc, Margin),
            zone, zone);
        foreach (var occurrence in occurrences)
        {
            if (SourceOf(occurrence, components) is not { } source) continue;
            if (component is not null && !ReferenceEquals(source, component)) continue;
            var (start, end) = Anchors(occurrence, zone);
            foreach (var alarm in source.Alarms)
            {
                if (FiresAt(alarm.Trigger, start, end, parsed, zone) is not { } first) continue;
                foreach (var at in Rings(first, alarm, toUtc))
                    if (at >= fromUtc && at < toUtc) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The instants one alarm rings at: its trigger, then RFC 5545 § 3.8.6.2's REPEAT more, spaced
    /// by DURATION — the two are inseparable, and either alone rings once. The count is bounded by
    /// the window rather than by the file: a REPEAT of a million is a body a client may send, and a
    /// query must stay finite whatever it is handed. <paramref name="toUtc"/> ends the sequence —
    /// a positive spacing makes it monotonic, so once one ring is past the window none can fall
    /// back inside — and <see cref="MaxRings"/> backstops a window that has no end.
    /// </summary>
    private static IEnumerable<DateTime> Rings(DateTime first, Alarm alarm, DateTime toUtc)
    {
        yield return first;
        if (alarm.Repeat <= 0 || alarm.Duration is not { } spacing) yield break;
        if (ToTimeSpan(spacing) is not { } step || step <= TimeSpan.Zero) yield break;

        var at = first;
        for (var i = 0; i < Math.Min(alarm.Repeat, MaxRings); i++)
        {
            at = Shift(at, step);
            if (at >= toUtc) yield break;
            yield return at;
        }
    }

    /// <summary>Null for a DURATION no TimeSpan holds: an unreadable spacing rings once, never a 500.</summary>
    private static TimeSpan? ToTimeSpan(Duration duration)
    {
        try
        {
            return duration.ToTimeSpanUnspecified();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The component an occurrence came from: the override it names, found by the id the
    /// walk read off it and never guessed from a time; else the master, else the first.</summary>
    internal static CalendarEvent? SourceOf(EventOccurrence occurrence, IReadOnlyList<CalendarEvent> components) =>
        (occurrence.IsOverride
            ? components.FirstOrDefault(c => IcsDocument.InstanceIdOf(c) == occurrence.InstanceId)
            : null)
        ?? components.FirstOrDefault(c => c.RecurrenceIdentifier is null)
        ?? components.FirstOrDefault();

    /// <summary>
    /// The instant an absent start walks from. Every instance a file can produce is sourced by a
    /// component's own DTSTART, RDATE or RECURRENCE-ID, so the earliest of those, less
    /// <see cref="Margin"/>, is below every instance the walk can find: the margin covers the
    /// offset a floating or zoned literal is posed through, which reaches 14 hours at the far side
    /// of the day. Ical.Net throws « un-representable DateTime » on a walk opened at
    /// <see cref="DateTime.MinValue"/>, and <see cref="Expansion.Periods"/> takes one margin more,
    /// hence the two the floor keeps above it.
    /// </summary>
    private static DateTime FloorOf(IcsCalendar parsed)
    {
        var earliest = IcsDocument.Components(parsed).SelectMany(SourceLiterals)
            .DefaultIfEmpty(DateTime.MaxValue)
            .Min();
        return Later(Shift(earliest, -Margin), Shift(DateTime.MinValue, 2 * Margin));
    }

    /// <summary>The literals a component sources its instances from, as the file spells them — a
    /// zone turns one into an instant, and only ever a later one than the margin allows for.</summary>
    private static IEnumerable<DateTime> SourceLiterals(CalendarEvent component)
    {
        if (component.DtStart is { } start) yield return start.Value;
        if (component.RecurrenceIdentifier?.StartTime is { } identified) yield return identified.Value;
        foreach (var date in component.RecurrenceDates?.GetAllDates() ?? []) yield return date.Value;
    }

    private static DateTime Earlier(DateTime left, DateTime right) => left < right ? left : right;

    private static DateTime Later(DateTime left, DateTime right) => left > right ? left : right;

    private static Expansion Over(Guid eventId, Guid calendarId, IcsCalendar parsed, DateTime fromUtc,
        DateTime toUtc, string calendarTimeZone, string viewTimeZone) =>
        new(
            eventId,
            calendarId,
            parsed,
            IcsDocument.MasterOf(parsed),
            IcsTimeZones.ResolveIana(calendarTimeZone) ?? IcsTimeZones.Utc,
            IcsTimeZones.ResolveIana(viewTimeZone) ?? IcsTimeZones.Utc,
            fromUtc,
            toUtc);

    /// <summary>
    /// The instants the window is judged on: a dated instance as it stands, an all-day one as the
    /// UTC midnights of the dates it names (RFC 4791 § 9.9's reading of a DATE value — the same
    /// day everywhere, never a local one), a floating one posed in <paramref name="zone"/>. This is
    /// also what a <c>free-busy-query</c> reads a busy period's own bounds from.
    /// </summary>
    internal static (DateTime StartUtc, DateTime EndUtc) Span(EventOccurrence occurrence, string zone) => occurrence switch
    {
        { IsAllDay: true } => (Midnight(occurrence.StartDate!.Value), Midnight(occurrence.EndDateExclusive!.Value)),
        { IsFloating: true } => (IcsTimeZones.ToUtc(occurrence.LocalStart!.Value, zone),
                                 IcsTimeZones.ToUtc(occurrence.LocalEnd!.Value, zone)),
        _ => (occurrence.StartUtc!.Value, occurrence.EndUtc!.Value),
    };

    /// <summary>The instants a trigger is measured from: an all-day or floating instance starts at
    /// its wall-clock reading in the calendar's zone, which is where its reminder rings.</summary>
    private static (DateTime Start, DateTime End) Anchors(EventOccurrence occurrence, string zone) => occurrence switch
    {
        { IsAllDay: true } => (IcsTimeZones.ToUtc(Midnight(occurrence.StartDate!.Value), zone),
                               IcsTimeZones.ToUtc(Midnight(occurrence.EndDateExclusive!.Value), zone)),
        { IsFloating: true } => (IcsTimeZones.ToUtc(occurrence.LocalStart!.Value, zone),
                                 IcsTimeZones.ToUtc(occurrence.LocalEnd!.Value, zone)),
        _ => (occurrence.StartUtc!.Value, occurrence.EndUtc!.Value),
    };

    /// <summary>Null for a trigger the engine cannot read — one without a distance, or one whose
    /// distance no TimeSpan holds: an alarm that never fires, never a 500.</summary>
    private static DateTime? FiresAt(Trigger? trigger, DateTime start, DateTime end, IcsCalendar parsed, string zone)
    {
        if (trigger is null) return null;
        if (trigger is { IsRelative: false, DateTime: { } at }) return IcsTimeZones.Place(at, zone, parsed).Utc;
        if (trigger.Duration is not { } distance) return null;
        try
        {
            var anchor = trigger.Related?.Equals(EndAnchor, StringComparison.OrdinalIgnoreCase) == true ? end : start;
            return Shift(anchor, distance.ToTimeSpanUnspecified());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>An instant moved by a margin and held inside the DateTime range: a bound at either
    /// edge widens to the edge, never to an exception.</summary>
    internal static DateTime Shift(DateTime at, TimeSpan margin) =>
        new(Math.Clamp(at.Ticks + margin.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);

    private sealed class Expansion(
        Guid eventId, Guid calendarId, IcsCalendar parsed, CalendarEvent? master, string calendarZone,
        string viewZone, DateTime fromUtc, DateTime toUtc)
    {
        private const string Opaque = "OPAQUE";

        private readonly bool recurring =
            master?.RecurrenceRule is not null || master?.RecurrenceDates?.GetAllDates().Any() == true;

        private readonly string? recurrenceText = master?.RecurrenceRule?.ToString();

        // The cap grows with the window, so an open one would not bound it at all: it is computed
        // on at most MaxSpan. A no-op for every closed window, all of which are refused past that.
        private int Cap => CapFor(fromUtc, Earlier(toUtc, Shift(fromUtc, MaxSpan)));

        /// <param name="firstOnly">stop at the first instance overlapping the window — a query
        /// asks whether one exists, never how many</param>
        internal IReadOnlyList<EventOccurrence> Run(bool firstOnly = false)
        {
            var found = new List<(DateTime At, EventOccurrence Occurrence)>();
            try
            {
                foreach (var (start, end, source) in Periods())
                {
                    var occurrence = Build(start, end, source);
                    var (at, until) = Span(occurrence);
                    if (at < toUtc && (until > at ? until : at.AddTicks(1)) > fromUtc)
                    {
                        found.Add((at, occurrence));
                        if (firstOnly) break;
                    }
                }
            }
            catch (Exception)
            {
                return [];
            }

            return found.OrderBy(f => f.At).Select(f => f.Occurrence).ToList();
        }

        /// <summary>
        /// The walk, widened a day on each side so that an instance straddling an edge is seen, and
        /// capped so that no rule can make it endless. A series
        /// <see cref="IcsGuards.IsWalkable(IcsCalendar)">nothing can expand</see> is never handed to
        /// the library — in Ical.Net 5.2.3 that is a stack overflow, which no catch block sees — and
        /// answers with the master's own instance instead. Stored resources all passed that gate;
        /// this is what keeps a resource stored before it did from taking the process down.
        /// </summary>
        private IEnumerable<(CalDateTime Start, CalDateTime? End, CalendarEvent Source)> Periods()
        {
            if (!IcsGuards.IsWalkable(parsed))
            {
                if (master?.DtStart is { } at) yield return (at, IcsDocument.EndOf(master), master);
                yield break;
            }

            // A TZID only the file's own VTIMEZONE defines makes Ical.Net throw rather than expand:
            // the walk then runs on a floating clone and every instant is posed back through that
            // block, so the event is on the grid instead of invisible (its wall clock never moves).
            var detached = IcsTimeZones.Detach(parsed);
            var walked = detached?.Calendar ?? parsed;
            var zones = detached?.Zones;

            var from = new CalDateTime(Shift(fromUtc, -Margin), IcsTimeZones.Utc);
            var to = new CalDateTime(Shift(toUtc, Margin), IcsTimeZones.Utc);
            foreach (var occurrence in walked.GetOccurrences(from).TakeWhileBefore(to).Take(Cap))
                if (occurrence.Period.StartTime is { } start && occurrence.Source is CalendarEvent source)
                    yield return (Posed(start, source, zones)!, Posed(occurrence.Period.EffectiveEndTime, source, zones), source);
        }

        private static CalDateTime? Posed(
            CalDateTime? at, CalendarEvent source, Dictionary<CalendarEvent, string>? zones) =>
            zones is not null && at is { HasTime: true, TzId: null or "" }
            && zones.TryGetValue(source, out var tzid)
                ? new CalDateTime(at.Value, tzid, true)
                : at;

        private EventOccurrence Build(CalDateTime start, CalDateTime? end, CalendarEvent source)
        {
            var last = end ?? start;
            var isOverride = source.RecurrenceIdentifier is not null;
            var allDay = !start.HasTime;
            var floating = !allDay && string.IsNullOrEmpty(start.TzId);
            var placed = allDay || floating ? default : IcsTimeZones.Place(start, calendarZone, parsed);

            return new EventOccurrence(
                eventId,
                calendarId,
                source.Uid ?? string.Empty,
                isOverride ? IcsDocument.InstanceIdOf(source) : recurring ? IcsDocument.LiteralOf(start) : string.Empty,
                isOverride,
                allDay,
                floating,
                placed.Zone,
                allDay || floating ? null : placed.Utc,
                allDay || floating ? null : IcsTimeZones.Place(last, calendarZone, parsed).Utc,
                allDay ? DateOnly.FromDateTime(start.Value) : null,
                allDay ? EndDateOf(start, last) : null,
                floating ? DateTime.SpecifyKind(start.Value, DateTimeKind.Unspecified) : null,
                floating ? DateTime.SpecifyKind(last.Value, DateTimeKind.Unspecified) : null,
                Trimmed(source.Summary),
                Trimmed(source.Location),
                Trimmed(source.Status)?.ToUpperInvariant(),
                Trimmed(source.Transparency)?.ToUpperInvariant() ?? Opaque,
                Trimmed(source.Class)?.ToUpperInvariant(),
                source.Alarms.Count > 0,
                recurrenceText);
        }

        private (DateTime At, DateTime Until) Span(EventOccurrence occurrence) =>
            OccurrenceExpander.Span(occurrence, viewZone);

        // A DTEND that does not outlive its DTSTART would name no day at all, and fall out of every
        // window; RFC 5545 § 3.6.1 makes an all-day event last at least the day it starts on.
        private static DateOnly EndDateOf(CalDateTime start, CalDateTime end)
        {
            var first = DateOnly.FromDateTime(start.Value);
            var after = DateOnly.FromDateTime(end.Value);
            return after > first ? after : first.AddDays(1);
        }

        private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
