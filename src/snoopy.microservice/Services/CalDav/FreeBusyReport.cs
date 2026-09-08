using System.Text;
using System.Xml.Linq;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The <c>CALDAV:free-busy-query</c> report (RFC 4791 § 7.10): one VFREEBUSY, never a multistatus,
/// on the calendar alone. The window is the client's own — no default around "now" — and, unlike
/// <c>calendar-query</c>, § 7.10 defines no precondition for a malformed one: a missing bound or
/// <c>end &lt;= start</c> is a bare 400, there being no filter to name.
/// </summary>
internal static class FreeBusyReport
{
    private const string Transparent = "TRANSPARENT";
    private const string Cancelled = "CANCELLED";
    private const string Tentative = "TENTATIVE";
    internal const string Busy = "BUSY";
    internal const string BusyTentative = "BUSY-TENTATIVE";

    private static readonly XName TimeRangeName = DavXml.CalDav + "time-range";

    internal sealed record BusyPeriod(DateTime StartUtc, DateTime EndUtc, string Type);

    /// <summary>
    /// § 7.10's table: TRANSPARENT and CANCELLED drop out, TENTATIVE becomes BUSY-TENTATIVE, the
    /// rest is BUSY. Contiguous or overlapping periods of the SAME type are coalesced ("SHOULD
    /// coalesce"); two different types never are, even when they overlap. Ordered by start.
    /// </summary>
    internal static IReadOnlyList<BusyPeriod> Periods(
        IEnumerable<EventOccurrence> occurrences, string calendarTimeZone)
    {
        var busy = new List<(DateTime Start, DateTime End, string Type)>();
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Transparency == Transparent || occurrence.Status == Cancelled) continue;
            var (start, end) = OccurrenceExpander.Span(occurrence, calendarTimeZone);
            busy.Add((start, end, occurrence.Status == Tentative ? BusyTentative : Busy));
        }

        return busy.GroupBy(p => p.Type)
            .SelectMany(group => Coalesce(group.OrderBy(p => p.Start)))
            .OrderBy(p => p.StartUtc)
            .ToList();
    }

    private static IEnumerable<BusyPeriod> Coalesce(
        IEnumerable<(DateTime Start, DateTime End, string Type)> ordered)
    {
        BusyPeriod? open = null;
        foreach (var (start, end, type) in ordered)
        {
            if (open is { } current && start <= current.EndUtc)
                open = current with { EndUtc = end > current.EndUtc ? end : current.EndUtc };
            else
            {
                if (open is not null) yield return open;
                open = new BusyPeriod(start, end, type);
            }
        }

        if (open is not null) yield return open;
    }

    /// <summary>The VCALENDAR text: DTSTAMP <paramref name="nowUtc"/>, DTSTART/DTEND the requested
    /// window, one FREEBUSY per period. The UID is Ical.Net's own, and it must be there:
    /// RFC 5545 § 3.6.4 makes it REQUIRED of a VFREEBUSY.</summary>
    internal static string Compose(
        DateTime fromUtc, DateTime toUtc, IReadOnlyList<BusyPeriod> periods, DateTime nowUtc)
    {
        var calendar = IcsComposer.Envelope();
        var freeBusy = new FreeBusy
        {
            DtStamp = IcsComposer.Utc(nowUtc),
            DtStart = IcsComposer.Utc(fromUtc),
            DtEnd = IcsComposer.Utc(toUtc),
        };
        foreach (var period in periods)
        {
            freeBusy.Entries.Add(new FreeBusyEntry(
                new Period(IcsComposer.Utc(period.StartUtc), IcsComposer.Utc(period.EndUtc)),
                period.Type == BusyTentative ? Ical.Net.FreeBusyStatus.BusyTentative : Ical.Net.FreeBusyStatus.Busy));
        }

        calendar.FreeBusy.Add(freeBusy);
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>The instances one query may weigh. MultigetReport.MaxHrefs bounds a multiget's
    /// members; this bounds what a single free-busy holds in memory before it writes.</summary>
    internal const int MaxInstances = 50_000;

    /// <summary>
    /// The window is bounded by <see cref="CalendarQueryFilter.ParseTimeRange"/> itself
    /// (<see cref="OccurrenceExpander.MaxSpan"/>), so a candidate's own walk stays inside
    /// <see cref="OccurrenceExpander.CapFor"/> — the same ceiling <c>calendar-data</c>'s
    /// <c>expand</c> answers to. <paramref name="upTo"/> is the counter read in the caller's own
    /// snapshot, as for <c>calendar-query</c>.
    /// </summary>
    internal static async Task WriteAsync(HttpResponse response, XDocument body, DavCalendar calendar,
        IDavCalendarReader reader, ulong upTo, TimeProvider clock, CancellationToken ct)
    {
        if (body.Root?.Element(TimeRangeName) is not { } element)
            throw new DavBadRequestException("A free-busy-query names no time-range.");
        var range = CalendarQueryFilter.ParseTimeRange(element, bothRequired: true, refusal: null);

        // The window is bounded, the TOTAL is not: five thousand resources times the cap of a
        // five-year window is tens of millions of instances, all held here before a single byte is
        // written — and an hourly rule walks through the PUT gate at 8760 a year. This is the one
        // report that composes its whole answer in memory, so it is the one that must count.
        // RFC 4791 § 7.10 names the refusal for exactly this case.
        var occurrences = new List<EventOccurrence>();
        await foreach (var candidate in reader.CandidatesAsync(calendar.Id, range.FromUtc, range.ToUtc,
            EventColumnFilter.None, upTo, ct).WithCancellation(ct))
        {
            // A stored resource is validated at PUT time; a file that no longer parses here is
            // database corruption, not a client's doing — left out, never a 500.
            if (IcsDocument.TryLoad(candidate.IcsRaw) is not { } parsed) continue;
            occurrences.AddRange(OccurrenceExpander.Expand(Guid.Empty, Guid.Empty, parsed,
                range.FromUtc, range.ToUtc, calendar.TimeZone, calendar.TimeZone));

            if (occurrences.Count > MaxInstances)
                throw new DavPreconditionException(CalDavError.NumberOfMatchesWithinLimits);
        }

        var text = Compose(range.FromUtc, range.ToUtc, Periods(occurrences, calendar.TimeZone),
            clock.GetUtcNow().UtcDateTime);
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = DavHeaders.FreeBusyContentType;
        response.ContentLength = bytes.Length;
        await response.Body.WriteAsync(bytes, ct);
    }
}
