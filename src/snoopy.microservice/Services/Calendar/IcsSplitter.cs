using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Snoopy.Microservice.Models.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// Scope ThisAndFollowing: the series is cut just before the chosen instance, and everything from
/// there on becomes a resource of its own under a new UID, shaped by the editor's values. The
/// exceptions past the cut follow when only the time of day moved and are let go otherwise.
/// </summary>
internal static class IcsSplitter
{
    internal static SplitOutcome Split(IcsCalendar existing, string instanceId, EventWrite w, string newUid, DateTime nowUtc)
    {
        var original = IcsComposer.Clone(existing);
        var master = IcsComposer.Master(original);
        var at = IcsComposer.InstanceAt(master, instanceId);
        var cut = IcsComposer.Instant(original, at);
        var counted = master.RecurrenceRule?.Count is > 0;
        var produced = ProducedBefore(original, master, cut);
        var oldCore = Core(master.RecurrenceRule);

        var following = IcsComposer.Clone(existing);
        var next = IcsComposer.Master(following);
        next.Uid = newUid;
        next.Created = IcsComposer.Utc(nowUtc);
        next.Sequence = 0;
        IcsComposer.Apply(next, Anchored(w, master.DtStart!, at), withRule: true);
        if (next.RecurrenceRule is { } rule)
        {
            if (counted) Carry(rule, produced);
            Realign(rule, at, next.DtStart!);
        }

        var rebase = next.RecurrenceRule is not null && at.HasTime == next.DtStart!.HasTime
                     && at.Date == next.DtStart.Date && Core(next.RecurrenceRule) == oldCore;
        var dropped = MoveExceptions(following, next, cut, rebase, newUid);
        IcsComposer.Stamp(next, nowUtc, bump: false);
        IcsComposer.EnsureTimeZones(following, IcsDocument.Components(following));

        KeepBefore(original, master, cut);
        if (master.RecurrenceRule is { } rest)
        {
            rest.Count = null;
            rest.Until = Before(at, cut);
        }

        IcsComposer.Stamp(master, nowUtc, bump: true);
        return new SplitOutcome(IcsDocument.Serialize(original), IcsDocument.Serialize(following), dropped);
    }

    /// <summary>A write still carrying the master's own DTSTART is the editor leaving the chosen
    /// instance where it was: it is read at that instance, at its own length. Any other start is
    /// the user's value, wherever it falls — the original is cut by UNTIL either way.</summary>
    private static EventWrite Anchored(EventWrite w, CalDateTime masterStart, CalDateTime at)
    {
        if (w.IsAllDay)
        {
            if (masterStart.HasTime || w.StartDate is not { } first || first != masterStart.Date) return w;
            var days = Math.Max(0, (w.EndDateInclusive ?? first).DayNumber - first.DayNumber);
            return w with { StartDate = at.Date, EndDateInclusive = at.Date.AddDays(days) };
        }

        if (!masterStart.HasTime || w.Start is not { } from || from != masterStart.Value
            || IcsTimeZones.ResolveIana(w.TimeZone) != IcsTimeZones.ResolveIana(masterStart.TzId)) return w;
        return w with { Start = at.Value, End = w.End is { } to ? at.Value + (to - from) : null };
    }

    /// <summary>How many instances the rule alone yields before the cut — EXDATEs included, since
    /// the rule produced them too, and RDATEs left out. Null when the rule has no COUNT, or when
    /// the series is one <see cref="IcsGuards.IsWalkable(IcsCalendar)"/> refuses to walk.</summary>
    private static int? ProducedBefore(IcsCalendar calendar, CalendarEvent master, DateTime cutUtc)
    {
        if (master is not { RecurrenceRule: { Count: > 0 } rule, DtStart: { } start } || !IcsGuards.IsWalkable(calendar)) return null;

        // A TZID only the file's own VTIMEZONE defines makes the walk throw: it runs floating and
        // each instant is posed back through that block before being weighed against the cut.
        var anchor = IcsTimeZones.Detached(start);
        var bare = new CalendarEvent { DtStart = anchor, RecurrenceRule = rule.Copy<RecurrenceRule>() };
        return bare.GetOccurrences(anchor).Take(rule.Count.Value)
            .TakeWhile(o => o.Period.StartTime is { } at
                            && IcsComposer.Instant(calendar, IcsTimeZones.Posed(at, start)) < cutUtc)
            .Count();
    }

    /// <summary>The COUNT the editor shows is the whole series'; the following part gets what the
    /// original has not produced yet — one at least, a finite series never becomes endless. When
    /// that could not be counted, the COUNT the editor sent stands.</summary>
    private static void Carry(RecurrenceRule rule, int? produced)
    {
        if (produced is { } n && rule.Count is { } count) rule.Count = Math.Max(1, count - n);
    }

    /// <summary>A weekly rule whose BYDAY named the instance's weekday follows the start to its new
    /// weekday.</summary>
    private static void Realign(RecurrenceRule rule, CalDateTime at, CalDateTime start)
    {
        if (rule.Frequency != FrequencyType.Weekly || rule.ByDay.Count == 0 || rule.ByDay.Any(d => d.DayOfWeek == start.DayOfWeek)) return;
        rule.ByDay.RemoveAll(d => d.DayOfWeek == at.DayOfWeek);
        rule.ByDay.Add(new WeekDay(start.DayOfWeek));
    }

    private static bool MoveExceptions(IcsCalendar following, CalendarEvent next, DateTime cutUtc, bool rebase, string newUid)
    {
        var dropped = false;
        foreach (var component in IcsDocument.Components(following).Where(c => c.RecurrenceIdentifier is not null).ToList())
        {
            var id = component.RecurrenceIdentifier!;
            if (IcsComposer.Instant(following, id.StartTime!) < cutUtc || !rebase)
            {
                dropped |= IcsComposer.Instant(following, id.StartTime!) >= cutUtc;
                IcsComposer.Detach(following, component);
                continue;
            }

            component.RecurrenceIdentifier = new RecurrenceIdentifier(Rebased(id.StartTime!, next.DtStart!), id.Range);
            component.Uid = newUid;
        }

        var exceptions = Later(following, next.ExceptionDates, cutUtc);
        var extras = Later(following, next.RecurrenceDates, cutUtc);
        dropped |= !rebase && (exceptions.Count > 0 || extras.Count > 0);
        IcsComposer.Rewrite(next.ExceptionDates, d => next.ExceptionDates.Add(Rebased(d, next.DtStart!)), rebase ? exceptions : []);
        IcsComposer.Rewrite(next.RecurrenceDates, d => next.RecurrenceDates.Add(Rebased(d, next.DtStart!)), rebase ? extras : []);
        return dropped;
    }

    private static List<CalDateTime> Later(IcsCalendar calendar, PeriodListWrapperBase? list, DateTime cutUtc) =>
        IcsComposer.Dates(list).Where(d => IcsComposer.Instant(calendar, d) >= cutUtc).ToList();

    /// <summary>The exceptions before the cut stay with the original; those at or after it leave.</summary>
    private static void KeepBefore(IcsCalendar original, CalendarEvent master, DateTime cutUtc)
    {
        foreach (var component in IcsDocument.Components(original).Where(c => c.RecurrenceIdentifier is not null).ToList())
            if (IcsComposer.Instant(original, component.RecurrenceIdentifier!.StartTime!) >= cutUtc)
                IcsComposer.Detach(original, component);

        IcsComposer.Rewrite(master.ExceptionDates, d => master.ExceptionDates.Add(d),
            IcsComposer.Dates(master.ExceptionDates).Where(d => IcsComposer.Instant(original, d) < cutUtc));
        IcsComposer.Rewrite(master.RecurrenceDates, d => master.RecurrenceDates.Add(d),
            IcsComposer.Dates(master.RecurrenceDates).Where(d => IcsComposer.Instant(original, d) < cutUtc));
    }

    /// <summary>The same day, at the new start's time of day and in its zone.</summary>
    private static CalDateTime Rebased(CalDateTime exception, CalDateTime start) =>
        start.HasTime
            ? new CalDateTime(exception.Date.ToDateTime(start.Time ?? TimeOnly.MinValue), start.TzId, true)
            : new CalDateTime(exception.Date);

    /// <summary>The last instant the original still covers: a second before the instance, in UTC
    /// when DTSTART names a zone; the day before for an all-day series.</summary>
    private static CalDateTime Before(CalDateTime at, DateTime cutUtc) => at switch
    {
        { HasTime: false } => new CalDateTime(at.Date.AddDays(-1)),
        { TzId: { Length: > 0 } } => IcsComposer.Utc(cutUtc.AddSeconds(-1)),
        _ => new CalDateTime(at.Value.AddSeconds(-1), null, true),
    };

    /// <summary>What a rule says apart from where it ends.</summary>
    private static string? Core(RecurrenceRule? rule) => rule is null ? null : string.Join("/",
        rule.Frequency, rule.Interval,
        string.Join(",", rule.ByDay.Select(d => $"{d.Offset}{d.DayOfWeek}")),
        string.Join(",", rule.ByMonthDay), string.Join(",", rule.BySetPosition), string.Join(",", rule.ByMonth));
}
