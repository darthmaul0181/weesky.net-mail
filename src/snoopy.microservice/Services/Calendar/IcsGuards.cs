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

    internal const string NoStart = "The event carries no start";

    private const string SupportedVersion = "2.0";
    private const string TzIdParameter = "TZID=";
    private const int MaxUidLength = 255;
    private const int MaxEmailLength = 320;

    /// <summary>One past the ceiling: the value every saturating count stops at, so no product of
    /// attacker-sized lists can ever wrap past it.</summary>
    private const long Ceiling = MaxInstancesPerYear + 1L;

    /// <summary>RFC 5545 § 3.3.11: the whole of what a backslash may introduce in a TEXT value.</summary>
    private const string Escapable = "\\;,nN";

    /// <summary>The properties whose value is TEXT and therefore obeys § 3.3.11's escaping.</summary>
    private static readonly HashSet<string> TextProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CALSCALE", "CATEGORIES", "CLASS", "COMMENT", "CONTACT", "DESCRIPTION", "LOCATION",
        "METHOD", "PRODID", "RELATED-TO", "RESOURCES", "STATUS", "SUMMARY", "TRANSP", "TZID",
        "TZNAME", "UID", "VERSION",
    };

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

    /// <summary>
    /// The whole judgement of one resource, in the order the store applies it: size — before the
    /// parse, which is the work an oversized body is trying to make us do — then syntax, version
    /// and shape, then the zones and the escapes the text owes, then density, then expansion, then
    /// the overrides the rule has to generate, then the DTSTART every VEVENT owes (RFC 5545
    /// § 3.6.1). Null when the file is accepted, <paramref name="parsed"/> then being its model.
    /// </summary>
    internal static IcsProblem? CheckAll(string ics, out IcsCalendar? parsed)
    {
        parsed = null;
        if (CheckSize(ics) is { } tooLarge) return tooLarge;

        parsed = IcsDocument.TryLoad(ics);
        return Check(ics, parsed) ?? CheckDensity(parsed!) ?? CheckExpansion(parsed!)
            ?? CheckOverrides(parsed!) ?? CheckStart(parsed!);
    }

    internal static IcsProblem? Check(string ics, IcsCalendar? parsed)
    {
        if (CheckSize(ics) is { } tooLarge) return tooLarge;
        if (parsed is null)
        {
            // Both are valid-calendar-data (§ 5.3.2.1 has no finer element), and both are refused
            // — but the reason a client is handed must be true. The DTSTART/DTEND type mismatch of
            // RFC 5545 § 3.8.2.2 lands here, and it IS iCalendar.
            return new IcsProblem(IcsPrecondition.ValidCalendarData,
                IcsDocument.LooksLikeCalendar(ics)
                    ? "The body is iCalendar text RFC 5545 refuses."
                    : "The body is not iCalendar text.");
        }
        if (parsed.Version != SupportedVersion)
            return new IcsProblem(IcsPrecondition.SupportedCalendarData, $"VERSION is '{parsed.Version}', not {SupportedVersion}.");
        // RFC 4791 § 4.1, MUST NOT: METHOD makes the object an iTIP message. RFC 6638 would give it
        // a meaning; we do not serve scheduling, so storing one would serve a message as an event.
        if (!string.IsNullOrEmpty(parsed.Method))
            return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource,
                $"A calendar object resource must not carry METHOD ('{parsed.Method}').");

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
        if (CheckZones(ics, parsed) is { } zone) return zone;
        return CheckTextEscapes(ics);
    }

    /// <summary>
    /// RFC 5545 § 3.2.19: an individual VTIMEZONE component MUST be specified for each unique
    /// TZID the object names. We announce neither RFC 7809's timezone service nor
    /// timezones-by-reference, so the file is everything a reader gets — one whose zone lives
    /// elsewhere cannot be resolved by anyone. Read off the text, not the model: Ical.Net resolves
    /// a known id against tzdb, and the parsed object then no longer remembers it was missing.
    /// </summary>
    private static IcsProblem? CheckZones(string ics, IcsCalendar parsed)
    {
        var defined = parsed.TimeZones.Select(zone => zone?.TzId).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var referenced in ReferencedTzIds(ics))
        {
            if (!defined.Contains(referenced))
                return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource,
                    $"TZID '{referenced}' is used but no VTIMEZONE in the object defines it.");
        }

        return null;
    }

    /// <summary>Every TZID parameter the text carries, VTIMEZONE blocks excepted — their own TZID
    /// is the definition, not a reference.</summary>
    private static IEnumerable<string> ReferencedTzIds(string ics)
    {
        var inZone = false;
        foreach (var line in Unfolded(ics))
        {
            if (line.StartsWith("BEGIN:VTIMEZONE", StringComparison.OrdinalIgnoreCase)) inZone = true;
            else if (line.StartsWith("END:VTIMEZONE", StringComparison.OrdinalIgnoreCase)) inZone = false;
            if (inZone) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            foreach (var parameter in line[..colon].Split(';').Skip(1))
            {
                if (parameter.StartsWith(TzIdParameter, StringComparison.OrdinalIgnoreCase))
                    yield return parameter[TzIdParameter.Length..].Trim('"');
            }
        }
    }

    /// <summary>
    /// RFC 5545 § 3.3.11: inside a TEXT value a backslash introduces one of five escapes and
    /// nothing else. A body carrying any other is not valid iCalendar, and what a reader makes of
    /// it differs from reader to reader. Only the properties whose value is TEXT are judged: an X-
    /// or IANA- property declares its type rather than owing one, and a URL or a DTSTART is not
    /// TEXT and carries no escaping rules at all.
    /// </summary>
    private static IcsProblem? CheckTextEscapes(string ics)
    {
        foreach (var line in Unfolded(ics))
        {
            if (NameOf(line) is not { } property || !TextProperties.Contains(property.Name)) continue;

            var value = line.AsSpan(property.Colon + 1);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\') continue;
                if (i + 1 >= value.Length || Escapable.IndexOf(value[i + 1]) < 0)
                    return new IcsProblem(IcsPrecondition.ValidCalendarData,
                        $"A {property.Name} value carries an escape RFC 5545 § 3.3.11 does not define.");
                i++;   // the escaped character is consumed, so a doubled backslash is one escape
            }
        }

        return null;
    }

    /// <summary>
    /// RFC 5545 § 3.8.4.4: an override's RECURRENCE-ID names an instance the master's rule
    /// generates. One that names no instance overrides nothing and is invisible from every window.
    /// Judged only where it can be: a resource with no master, one whose master repeats not at all,
    /// or a series <see cref="IsWalkable(IcsCalendar)"/> refuses is left alone — a guard that must
    /// walk in order to refuse never refuses what it could not walk.
    /// </summary>
    private static IcsProblem? CheckOverrides(IcsCalendar parsed)
    {
        if (!IsWalkable(parsed)) return null;
        if (IcsDocument.MasterOf(parsed) is not { DtStart: not null } master) return null;
        if (master.RecurrenceRule is null && master.RecurrenceDates?.GetAllDates().Any() != true) return null;
        if (!IcsDocument.Components(parsed).Any(HasInstanceId)) return null;

        // The clone is walked without its overrides: attached, the library answers each of them as
        // an occurrence of its own and the identifier under judgement would always be in the set.
        var series = IcsTimeZones.Detach(parsed)?.Calendar ?? IcsComposer.Clone(parsed);
        var overrides = IcsDocument.Components(series).Where(HasInstanceId).ToList();
        var latest = overrides.Max(c => IcsComposer.Instant(series, c.RecurrenceIdentifier!.StartTime!));
        foreach (var component in overrides) IcsComposer.Detach(series, component);
        if (InstanceIds(series, IcsDocument.MasterOf(series)!.DtStart!, latest) is not { } ids) return null;

        foreach (var component in overrides)
        {
            var id = IcsDocument.InstanceIdOf(component);
            if (!ids.Contains(id))
                return new IcsProblem(IcsPrecondition.ValidCalendarData,
                    $"RECURRENCE-ID '{id}' names no instance of the series.");
        }

        return null;
    }

    private static bool HasInstanceId(CalendarEvent component) => component.RecurrenceIdentifier?.StartTime is not null;

    /// <summary>
    /// The instance identifiers the master alone generates, walked until it is past
    /// <paramref name="latest"/> so that every override the file carries falls inside the window.
    /// Null when the walk threw, or when the density ceiling stopped it first: an identifier the
    /// window never reached is one this guard cannot judge, and the file is left to the others.
    /// </summary>
    private static HashSet<string>? InstanceIds(IcsCalendar series, CalDateTime start, DateTime latest)
    {
        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var walked = 0;
            foreach (var occurrence in series.GetOccurrences(IcsTimeZones.Detached(start)).Take(MaxInstancesPerYear))
            {
                walked++;
                if (occurrence.Period.StartTime is not { } at) continue;
                ids.Add(IcsDocument.LiteralOf(at));
                if (IcsComposer.Instant(series, at) > latest) return ids;
            }

            return walked < MaxInstancesPerYear ? ids : null;
        }
        catch (Exception)
        {
            return null;
        }
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
    /// Nothing above reads DTSTART: <see cref="Check"/> judges shape and identity,
    /// <see cref="CheckExpansion"/> answers null when there is no start to walk from. Called last,
    /// on a resource already known well-formed — without it a VEVENT with no start would be stored
    /// at <c>NoInstant</c>, visible from no window, no query and no screen.
    /// </summary>
    internal static IcsProblem? CheckStart(IcsCalendar parsed) =>
        (IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).First()).DtStart is null
            ? new IcsProblem(IcsPrecondition.ValidCalendarData, NoStart)
            : null;

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
        foreach (var line in Unfolded(ics))
        {
            if (line.StartsWith("BEGIN:VALARM", StringComparison.OrdinalIgnoreCase)) inAlarm = true;
            else if (line.StartsWith("END:VALARM", StringComparison.OrdinalIgnoreCase)) inAlarm = false;
            if (inAlarm) continue;

            if (NameOf(line) is not { } property || line.AsSpan(property.Colon + 1).Trim().Length == 0) continue;
            if (property.Name.Equals("UID", StringComparison.OrdinalIgnoreCase)) written++;
        }

        return written;
    }

    /// <summary>The file's logical lines, RFC 5545 § 3.1's folding undone, so a property split over
    /// several physical lines is read as the one line it is.</summary>
    private static IEnumerable<string> Unfolded(string ics) =>
        ics.Replace("\r\n", "\n").Replace("\n ", string.Empty).Replace("\n\t", string.Empty).Split('\n');

    /// <summary>The property name a logical line opens with and where its value starts, or null
    /// when the line names no property.</summary>
    private static (string Name, int Colon)? NameOf(string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0) return null;
        var semicolon = line.AsSpan(0, colon).IndexOf(';');
        return (line[..(semicolon < 0 ? colon : semicolon)], colon);
    }
}
