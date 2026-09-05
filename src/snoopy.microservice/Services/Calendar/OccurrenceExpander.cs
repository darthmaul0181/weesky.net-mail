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
/// </summary>
internal static class OccurrenceExpander
{
    /// <summary>The span an API window may cover. The controller enforces it; the walk below stays
    /// finite for any input on its own.</summary>
    internal const int MaxYears = 5;

    /// <summary>
    /// The window is <c>[fromUtc, toUtc[</c>. <paramref name="calendarTimeZone"/> poses what the
    /// file left unposed, <paramref name="viewTimeZone"/> cuts the days a floating instance falls
    /// on. All-day dates are read as dates — UTC midnights, the reading RFC 4791 § 9.9 gives a
    /// DATE value — so the same day is the same day for every reader.
    /// </summary>
    internal static IReadOnlyList<EventOccurrence> Expand(
        Guid eventId, Guid calendarId, IcsCalendar parsed, DateTime fromUtc, DateTime toUtc,
        string calendarTimeZone, string viewTimeZone) =>
        new Expansion(
            eventId,
            calendarId,
            parsed,
            IcsDocument.MasterOf(parsed),
            IcsTimeZones.ResolveIana(calendarTimeZone) ?? IcsTimeZones.Utc,
            IcsTimeZones.ResolveIana(viewTimeZone) ?? IcsTimeZones.Utc,
            fromUtc,
            toUtc).Run();

    private sealed class Expansion(
        Guid eventId, Guid calendarId, IcsCalendar parsed, CalendarEvent? master, string calendarZone,
        string viewZone, DateTime fromUtc, DateTime toUtc)
    {
        private const string Opaque = "OPAQUE";
        private static readonly TimeSpan Margin = TimeSpan.FromDays(1);

        private readonly bool recurring =
            master?.RecurrenceRule is not null || master?.RecurrenceDates?.GetAllDates().Any() == true;

        private readonly string? recurrenceText = master?.RecurrenceRule?.ToString();

        /// <summary>10 000 instances per year of window, the density the PUT gate admits, plus the
        /// one that proves the ceiling was reached.</summary>
        private int Cap => IcsGuards.MaxInstancesPerYear
                           * Math.Max(1, (int)Math.Ceiling((toUtc - fromUtc).TotalDays / 365.2425)) + 1;

        internal IReadOnlyList<EventOccurrence> Run()
        {
            var found = new List<(DateTime At, EventOccurrence Occurrence)>();
            try
            {
                foreach (var (start, end, source) in Periods())
                {
                    var occurrence = Build(start, end, source);
                    var (at, until) = Span(occurrence);
                    if (at < toUtc && (until > at ? until : at.AddTicks(1)) > fromUtc) found.Add((at, occurrence));
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

            var from = new CalDateTime(Widen(fromUtc, -Margin), IcsTimeZones.Utc);
            var to = new CalDateTime(Widen(toUtc, Margin), IcsTimeZones.Utc);
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

        /// <summary>The instants the window is judged on: a dated instance as it stands, an all-day
        /// one as the dates it names, a floating one posed in the reader's zone.</summary>
        private (DateTime At, DateTime Until) Span(EventOccurrence occurrence) => occurrence switch
        {
            { IsAllDay: true } => (Midnight(occurrence.StartDate!.Value), Midnight(occurrence.EndDateExclusive!.Value)),
            { IsFloating: true } => (IcsTimeZones.ToUtc(occurrence.LocalStart!.Value, viewZone),
                                     IcsTimeZones.ToUtc(occurrence.LocalEnd!.Value, viewZone)),
            _ => (occurrence.StartUtc!.Value, occurrence.EndUtc!.Value),
        };

        // A DTEND that does not outlive its DTSTART would name no day at all, and fall out of every
        // window; RFC 5545 § 3.6.1 makes an all-day event last at least the day it starts on.
        private static DateOnly EndDateOf(CalDateTime start, CalDateTime end)
        {
            var first = DateOnly.FromDateTime(start.Value);
            var after = DateOnly.FromDateTime(end.Value);
            return after > first ? after : first.AddDays(1);
        }

        private static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        private static DateTime Widen(DateTime at, TimeSpan margin) =>
            new(Math.Clamp(at.Ticks + margin.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);

        private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
