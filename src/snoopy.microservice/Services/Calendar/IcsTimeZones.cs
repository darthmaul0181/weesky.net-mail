using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime;
using NodaTime.TimeZones;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// Zone identifiers, in three tiers: an IANA id TZDB knows, a Windows name the CLDR mapping turns
/// into one, and — for anything left, which only <see cref="OffsetOf"/> answers — the VTIMEZONE the
/// file carries. TZDB, never <see cref="TimeZoneInfo"/>: the host's own database differs by
/// platform and by patch level, and a stored instant may not.
/// </summary>
internal static class IcsTimeZones
{
    internal const string Utc = "UTC";

    /// <summary>The one message every caller of <see cref="IsKnownIana"/> refuses with, written
    /// once so the controllers and the validator cannot spell it three different ways.</summary>
    internal const string UnknownZone = "Unknown time zone";

    // A VTIMEZONE whose transitions outnumber this is not a zone, it is a denial of service.
    private const int MaxTransitions = 2000;

    // Every instant this class produces carries Kind Utc, this one included: a date nobody wrote.
    private static readonly DateTime NoInstant = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    /// <summary>An instant, the IANA zone it was read in (null when floating, all-day, or named by a
    /// TZID nothing resolves), and whether the component named such a TZID.</summary>
    internal readonly record struct Placed(DateTime Utc, string? Zone, bool Unknown);

    /// <summary>
    /// The four tiers a moment is read through, in the order that costs the least: an IANA id, a
    /// Windows name the CLDR mapping turns into one, the VTIMEZONE the file itself carries, and —
    /// for a TZID none of them answers — the calendar's own zone. A date without a time and a
    /// floating reading are posed in <paramref name="calendarZone"/> (décision 5).
    /// </summary>
    internal static Placed Place(CalDateTime? at, string calendarZone, IcsCalendar parsed)
    {
        if (at is null) return new Placed(NoInstant, null, false);
        if (!at.HasTime || string.IsNullOrEmpty(at.TzId))
            return new Placed(ToUtc(at.Value, calendarZone), null, false);
        if (ResolveIana(at.TzId) is { } iana)
            return new Placed(ToUtc(at.Value, iana), iana, false);
        if (FileZone(parsed, at.TzId) is { } zone && OffsetOf(zone, at.Value) is { } offset)
            return new Placed(DateTime.SpecifyKind(at.Value - offset, DateTimeKind.Utc), null, true);
        return new Placed(ToUtc(at.Value, calendarZone), null, true);
    }

    internal static string? ResolveIana(string? tzid)
    {
        if (string.IsNullOrWhiteSpace(tzid)) return null;
        var id = tzid.Trim();
        return IsKnownIana(id) ? id : WindowsMapping.TryGetValue(id, out var mapped) ? mapped : null;
    }

    /// <summary>The TZID the moment names when neither TZDB nor the Windows mapping answers it —
    /// the third tier, which only <see cref="OffsetOf"/> and the file's own block can read.</summary>
    internal static string? Unresolved(CalDateTime? at) =>
        at is { HasTime: true, TzId: { Length: > 0 } tzid } && ResolveIana(tzid) is null ? tzid : null;

    /// <summary>A component Ical.Net can expand: it throws "Unrecognized time zone id" on any
    /// moment of the third tier rather than applying the block it has just loaded.</summary>
    internal static bool Expandable(CalendarEvent component) =>
        Unresolved(component.DtStart) is null && Unresolved(component.DtEnd) is null
        && Unresolved(component.RecurrenceIdentifier?.StartTime) is null
        && (component.RecurrenceDates?.GetAllDates() ?? []).All(d => Unresolved(d) is null)
        && (component.ExceptionDates?.GetAllDates() ?? []).All(d => Unresolved(d) is null);

    /// <summary>The same wall clock, floating, when the zone is one Ical.Net would throw on.</summary>
    internal static CalDateTime Detached(CalDateTime at) =>
        Unresolved(at) is null ? at : new CalDateTime(at.Value, null, true);

    /// <summary>The inverse: a floating instant produced by a detached walk, put back in the zone
    /// <paramref name="model"/> was read in, so the file's own VTIMEZONE applies to it.</summary>
    internal static CalDateTime Posed(CalDateTime at, CalDateTime? model) =>
        Unresolved(model) is { } tzid && at is { HasTime: true, TzId: null or "" }
            ? new CalDateTime(at.Value, tzid, true)
            : at;

    /// <summary>
    /// The clone a third-tier resource is walked through — every unresolvable moment floating, its
    /// wall clock untouched — and the zone each component was read in, so
    /// <see cref="Posed(CalDateTime, CalDateTime?)"/> can put every produced instant back. Null
    /// when nothing needs detaching, which is every file but the rare one.
    /// </summary>
    internal static (IcsCalendar Calendar, Dictionary<CalendarEvent, string> Zones)? Detach(IcsCalendar parsed)
    {
        if (IcsDocument.Components(parsed).All(Expandable)) return null;

        var clone = IcsComposer.Clone(parsed);
        var zones = new Dictionary<CalendarEvent, string>();
        foreach (var component in IcsDocument.Components(clone))
        {
            if (Unresolved(component.DtStart) is { } tzid) zones[component] = tzid;
            if (component.DtStart is { } start) component.DtStart = Detached(start);
            if (component.DtEnd is { } end) component.DtEnd = Detached(end);
            if (component.RecurrenceIdentifier is { StartTime: { } id } recurrence)
                component.RecurrenceIdentifier = new RecurrenceIdentifier(Detached(id), recurrence.Range);
            Loosen(component.RecurrenceDates, d => component.RecurrenceDates.Add(d));
            Loosen(component.ExceptionDates, d => component.ExceptionDates.Add(d));
        }

        return (clone, zones);
    }

    private static void Loosen(PeriodListWrapperBase? list, Action<CalDateTime> add) =>
        IcsComposer.Rewrite(list, add, IcsComposer.Dates(list).Select(Detached).ToList());

    internal static bool IsKnownIana(string id) => DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is not null;

    /// <summary>
    /// The block for a zone from <paramref name="earliestUtc"/> on. Ical.Net 5.2.3 walks the
    /// transitions from a year before the instant it is given and assumes a step of +1h into the
    /// first interval it sees — right for a standard one, an hour off for a daylight one. The
    /// instant handed over is therefore the middle of the standard interval in force at or before
    /// <paramref name="earliestUtc"/>: a year earlier it is still standard time, whatever day the
    /// transitions fell on that year.
    /// </summary>
    internal static VTimeZone Emit(string ianaId, DateTime earliestUtc)
    {
        var zone = Zone(ianaId);
        var interval = zone.GetZoneInterval(Instant.FromDateTimeUtc(DateTime.SpecifyKind(earliestUtc, DateTimeKind.Utc)));
        while (interval.Savings != Offset.Zero && interval.HasStart)
            interval = zone.GetZoneInterval(interval.Start - NodaTime.Duration.Epsilon);
        var from = interval switch
        {
            { HasStart: true, HasEnd: true } => interval.Start + interval.Duration / 2,
            { HasStart: true } => interval.Start + NodaTime.Duration.FromDays(1),
            _ => Instant.FromDateTimeUtc(DateTime.SpecifyKind(earliestUtc, DateTimeKind.Utc)),
        };
        return VTimeZone.FromDateTimeZone(ianaId, from.ToDateTimeUtc(), false);
    }

    /// <summary>A wall-clock reading in a zone, as an instant. A local time the zone skips moves
    /// forward past the gap, and an ambiguous one takes its first reading.</summary>
    internal static DateTime ToUtc(DateTime local, string ianaId) =>
        LocalDateTime.FromDateTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified))
            .InZoneLeniently(Zone(ianaId)).ToDateTimeUtc();

    internal static DateTime FromUtc(DateTime utc, string ianaId) =>
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            .InZone(Zone(ianaId)).ToDateTimeUnspecified();

    /// <summary>
    /// What a VTIMEZONE block says the offset is at a local time: the OFFSETTO of the latest
    /// transition at or before it, or the OFFSETFROM of the earliest when the time predates them
    /// all. Null when the block declares no observance. The third tier — only reached for a TZID
    /// neither TZDB nor the Windows mapping resolves, so its cost is paid by nobody else.
    /// </summary>
    internal static TimeSpan? OffsetOf(VTimeZone zone, DateTime local)
    {
        var boundary = new CalDateTime(local);
        DateTime? latest = null;
        DateTime? earliest = null;
        TimeSpan? atLatest = null;
        TimeSpan? beforeEarliest = null;
        foreach (var observance in zone.Children.OfType<VTimeZoneInfo>())
        {
            if (observance.DtStart is not { } start) continue;
            if (earliest is null || start.Value < earliest)
            {
                earliest = start.Value;
                beforeEarliest = observance.OffsetFrom?.Offset;
            }

            DateTime? transition;
            // An observance the library refuses to evaluate contributes nothing rather than taking
            // the whole zone down with it; the remaining ones still answer.
            try
            {
                transition = observance.GetOccurrences(start).TakeWhileBefore(boundary)
                    .Take(MaxTransitions).LastOrDefault()?.Period.StartTime?.Value;
            }
            catch (Exception) { continue; }

            if (transition is { } at && (latest is null || at > latest))
            {
                latest = at;
                atLatest = observance.OffsetTo?.Offset;
            }
        }

        return atLatest ?? beforeEarliest;
    }

    private static VTimeZone? FileZone(IcsCalendar parsed, string tzid) =>
        parsed.TimeZones.FirstOrDefault(z => z?.TzId == tzid);

    private static IDictionary<string, string> WindowsMapping =>
        TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping;

    private static DateTimeZone Zone(string ianaId) =>
        DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaId) ?? DateTimeZone.Utc;
}
