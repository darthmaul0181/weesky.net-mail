using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// The PUT gate: everything RFC 4791 lets a server refuse, judged in the order that names the real
/// cause — a body too large is not "invalid", and a VTODO is not "not a resource".
/// </summary>
internal static class IcsGuards
{
    internal const int MaxIcsBytes = 1024 * 1024;
    internal const int MaxInstancesPerYear = 10_000;

    private const string SupportedVersion = "2.0";
    private const int MaxUidLength = 255;
    private const int MaxEmailLength = 320;

    /// <summary>One past the ceiling: the value every saturating count stops at, so no product of
    /// attacker-sized lists can ever wrap past it.</summary>
    private const long Ceiling = MaxInstancesPerYear + 1L;

    /// <summary>
    /// The one precondition that must be judged <b>before</b> the body is parsed, and the reason it
    /// stands alone: parsing is the work an oversized body is trying to make us do.
    /// </summary>
    internal static IcsProblem? CheckSize(string ics)
    {
        var bytes = Encoding.UTF8.GetByteCount(ics);
        return bytes > MaxIcsBytes
            ? new IcsProblem(IcsPrecondition.MaxResourceSize, $"The resource is {bytes} bytes, over the {MaxIcsBytes} allowed.")
            : null;
    }

    internal static IcsProblem? Check(string ics, IcsCalendar? parsed)
    {
        if (CheckSize(ics) is { } tooLarge) return tooLarge;
        if (parsed is null)
            return new IcsProblem(IcsPrecondition.ValidCalendarData, "The body is not iCalendar text.");
        if (parsed.Version != SupportedVersion)
            return new IcsProblem(IcsPrecondition.SupportedCalendarData, $"VERSION is '{parsed.Version}', not {SupportedVersion}.");

        var components = IcsDocument.Components(parsed).ToList();
        var unsupported = parsed.Todos.Count > 0 || parsed.Journals.Count > 0 || parsed.FreeBusy.Count > 0;
        // The two are different refusals: a collection holding nothing we store names the component
        // it holds, while one mixing a VTODO into a VEVENT resource is not a resource at all.
        if (components.Count == 0)
            return unsupported
                ? new IcsProblem(IcsPrecondition.SupportedCalendarComponent, "The collection holds VEVENT only, not VTODO, VJOURNAL or VFREEBUSY.")
                : new IcsProblem(IcsPrecondition.ValidCalendarObjectResource, "The resource carries no VEVENT.");
        if (unsupported)
            return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource, "The resource puts a VTODO, VJOURNAL or VFREEBUSY beside its VEVENT.");
        if (components.Select(e => e.Uid).Distinct(StringComparer.Ordinal).Count() > 1)
            return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource, "The components do not share one UID.");
        if (components.Count(e => e.RecurrenceIdentifier is null) > 1)
            return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource, "The resource carries more than one component without a RECURRENCE-ID.");
        if (WrittenUids(ics) < components.Count)
            return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource, "A component carries no UID.");
        if (components.Any(TooLong))
            return new IcsProblem(IcsPrecondition.ValidCalendarData, "A UID or attendee address is too long");
        return null;
    }

    /// <summary>The DDL widths <c>uid VARCHAR(255)</c> and <c>calendar_attendees.email
    /// VARCHAR(320)</c>: neither may be cut, since one is the identity a client syncs on and the
    /// other the person addressed — so the resource is refused instead.</summary>
    private static bool TooLong(CalendarEvent component) =>
        component.Uid?.Length > MaxUidLength
        || Addresses(component).Any(address => address.Length > MaxEmailLength);

    private static IEnumerable<string> Addresses(CalendarEvent component) =>
        (component.Attendees ?? []).Where(a => a is not null).Select(a => IcsProjector.Address(a.Value))
            .Append(IcsProjector.Address(component.Organizer?.Value))
            .OfType<string>();

    /// <summary>
    /// Décision 4 again, tried rather than counted: one instance is asked for, so a resource whose
    /// rule the engine cannot evaluate is refused at the door instead of being stored and found
    /// unreadable by every window afterwards. Never run on a series
    /// <see cref="IsWalkable(IcsCalendar)"/> refuses — that one is a stack overflow, not a throw.
    /// </summary>
    internal static IcsProblem? CheckExpansion(IcsCalendar parsed)
    {
        if (!IsWalkable(parsed)) return null;

        var master = IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).FirstOrDefault();
        if (master?.DtStart is not { } start) return null;

        try
        {
            var walked = IcsTimeZones.Detach(parsed)?.Calendar ?? parsed;
            walked.GetOccurrences(IcsTimeZones.Detached(start)).Take(1).ToList();
            return null;
        }
        catch (Exception)
        {
            return new IcsProblem(IcsPrecondition.ValidCalendarData, "The recurrence cannot be expanded");
        }
    }

    /// <summary>
    /// Décision 4: the ceiling is a density, not a total — ten thousand instances inside the year
    /// that follows DTSTART. A rule that runs for a century at one a day is a calendar; one that
    /// fires every second for a week is an attack. A series
    /// <see cref="IsWalkable(IcsCalendar)">nothing can expand</see> is refused here too, since a
    /// resource whose instances cannot be counted at all is the same answer to the same question.
    /// </summary>
    internal static IcsProblem? CheckDensity(IcsCalendar parsed)
    {
        var instances = 0L;
        foreach (var component in IcsDocument.Components(parsed))
        {
            if (!IsWalkable(component))
                return new IcsProblem(IcsPrecondition.MaxInstances,
                    $"A rule repeating more than once a day in time zone '{component.DtStart?.TzId}' cannot be expanded.");

            instances += component.RecurrenceDates?.GetAllDates().Count() ?? 0;
            if (component is { RecurrenceRule: { } rule, DtStart: { } start }) instances += InstancesInAYear(rule, start);
            if (instances > MaxInstancesPerYear)
                return new IcsProblem(IcsPrecondition.MaxInstances, $"Over {MaxInstancesPerYear} instances in the year following DTSTART.");
        }

        return null;
    }

    /// <summary>
    /// Whether the series may be expanded at all. Ical.Net 5.2.3 dies — a stack overflow, not an
    /// exception, so no catch block sees it — walking a rule that repeats more than once a day in a
    /// named zone, at the hour a DST fall-back makes the wall clock read twice. UTC and floating
    /// have no such hour and are always walkable. The single place that knows this.
    /// </summary>
    internal static bool IsWalkable(IcsCalendar parsed) => IcsDocument.Components(parsed).All(IsWalkable);

    private static bool IsWalkable(CalendarEvent component) =>
        component.RecurrenceRule is not { } rule || !RepeatsWithinADay(rule) || !IsZoned(component.DtStart);

    // The effective spacing, not the FREQ token: BYHOUR/BYMINUTE/BYSECOND put a second instance
    // inside the day of a rule whose frequency alone would never do so.
    private static bool RepeatsWithinADay(RecurrenceRule rule) =>
        rule.Frequency is FrequencyType.Secondly or FrequencyType.Minutely or FrequencyType.Hourly
        || Count(rule.ByHour) > 0 || Count(rule.ByMinute) > 0 || Count(rule.BySecond) > 0;

    private static bool IsZoned(CalDateTime? at) =>
        at is { HasTime: true, TzId: { Length: > 0 } tzId } && tzId != IcsTimeZones.Utc;

    /// <summary>
    /// The rule is counted, not walked: a guard that has to run the attack in order to refuse it is
    /// not a guard. The BY* parts multiply where RFC 5545 § 3.3.10 makes them expand the frequency
    /// they sit on, and are ignored where they only limit it.
    /// </summary>
    private static long InstancesInAYear(RecurrenceRule rule, CalDateTime start)
    {
        var perYear = rule.Frequency switch
        {
            FrequencyType.Secondly => 365L * 24 * 60 * 60,
            FrequencyType.Minutely => 365L * 24 * 60,
            FrequencyType.Hourly => 365L * 24,
            FrequencyType.Daily => 365L,
            FrequencyType.Weekly => 53L,
            FrequencyType.Monthly => 12L,
            _ => 1L,
        } / Math.Max(1, rule.Interval) * Expansion(rule);

        if (rule.Until?.Value is { } until && until < start.Value.AddYears(1))
            perYear = (long)Math.Ceiling(perYear * Math.Max(0, (until - start.Value).TotalDays) / 365);
        return rule.Count is { } count and > 0 ? Math.Min(perYear, count) : perYear;
    }

    /// <summary>
    /// The BY* parts that multiply this frequency, folded with a <b>saturating</b> product: clamping
    /// each factor is not enough, because eight of them wrap a <c>long</c> — 256^8 is exactly 2^64,
    /// so eight legal 256-entry lists used to count as no instances at all and walk straight past
    /// the ceiling. Stopping at the ceiling + 1 makes the product monotonic and unwrappable.
    /// </summary>
    private static long Expansion(RecurrenceRule rule)
    {
        int[] factors = rule.Frequency switch
        {
            FrequencyType.Secondly => [],
            FrequencyType.Minutely => [Count(rule.BySecond)],
            FrequencyType.Hourly => [Count(rule.BySecond), Count(rule.ByMinute)],
            FrequencyType.Daily => [Count(rule.BySecond), Count(rule.ByMinute), Count(rule.ByHour)],
            FrequencyType.Weekly => [Count(rule.BySecond), Count(rule.ByMinute), Count(rule.ByHour), Count(rule.ByDay)],
            FrequencyType.Monthly =>
            [
                Count(rule.BySecond), Count(rule.ByMinute), Count(rule.ByHour),
                Count(rule.ByDay), Count(rule.ByMonthDay),
            ],
            _ =>
            [
                Count(rule.BySecond), Count(rule.ByMinute), Count(rule.ByHour), Count(rule.ByDay),
                Count(rule.ByMonthDay), Count(rule.ByYearDay), Count(rule.ByWeekNo), Count(rule.ByMonth),
            ],
        };

        var product = 1L;
        foreach (var factor in factors)
            product = Math.Min(product * Math.Clamp(factor, 1, Ceiling), Ceiling);
        return product;
    }

    private static int Count<T>(IList<T>? values) => values?.Count ?? 0;

    /// <summary>
    /// UID lines the client actually wrote. Ical.Net fabricates one for a component that carries
    /// none, so the parsed model cannot tell a real UID from an invented one — this is the single
    /// place the calendar engine reads iCalendar text by hand, and the reason it has to. A VALARM
    /// carries a UID of its own (Google writes one, iOS writes it beside X-WR-ALARMUID) and that
    /// line answers for no component: the alarm's own block is skipped whole.
    /// </summary>
    private static int WrittenUids(string ics)
    {
        var written = 0;
        var inAlarm = false;
        var unfolded = ics.Replace("\r\n", "\n").Replace("\n ", string.Empty).Replace("\n\t", string.Empty);
        foreach (var line in unfolded.Split('\n'))
        {
            if (line.StartsWith("BEGIN:VALARM", StringComparison.OrdinalIgnoreCase)) inAlarm = true;
            else if (line.StartsWith("END:VALARM", StringComparison.OrdinalIgnoreCase)) inAlarm = false;
            if (inAlarm) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0 || line.AsSpan(colon + 1).Trim().Length == 0) continue;
            var name = line.AsSpan(0, colon);
            var semicolon = name.IndexOf(';');
            if (semicolon >= 0) name = name[..semicolon];
            if (name.Equals("UID", StringComparison.OrdinalIgnoreCase)) written++;
        }

        return written;
    }
}
