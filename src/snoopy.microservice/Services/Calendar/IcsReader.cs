using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using System.Globalization;
using weesky.Snoopy.Microservice.Models.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>
/// The inverse of <see cref="IcsComposer.Apply"/>: a stored resource read back into the shape the
/// editor sends. What the editor cannot express — X- lines, foreign alarms, the overrides of a
/// series — is deliberately absent here and rides through the composer untouched, so opening an
/// event and saving it without a change writes the same event.
/// </summary>
internal static class IcsReader
{
    private const string Tentative = "TENTATIVE";
    private const string Transparent = "TRANSPARENT";
    private const string Private = "PRIVATE";
    private const string Confidential = "CONFIDENTIAL";
    private const string UnnamedAction = "ALARM";
    private const string EndAnchor = "END";
    private const string AbsoluteFormat = "yyyy-MM-dd HH:mm";

    /// <summary>The four <see cref="IcsComposer.RuleOf"/> knows how to write back.</summary>
    private static readonly FrequencyType[] EditorFrequencies =
        [FrequencyType.Daily, FrequencyType.Weekly, FrequencyType.Monthly, FrequencyType.Yearly];

    private static readonly EventWrite Nothing = new(
        Guid.Empty, null, null, null, false, null, null, null, null, null, null, [],
        Availability.Busy, Visibility.Default, null);

    internal static EventWrite Read(IcsCalendar parsed, Guid calendarId)
    {
        var master = IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).FirstOrDefault();
        if (master?.DtStart is not { } start) return Nothing with { CalendarId = calendarId };

        var end = IcsDocument.EndOf(master);
        var allDay = !start.HasTime;

        return new EventWrite(
            calendarId,
            Trimmed(master.Summary),
            Trimmed(master.Location),
            Trimmed(master.Description),
            allDay,
            allDay ? null : Wall(start),
            allDay || end is null ? null : Wall(end),
            allDay ? null : IcsTimeZones.ResolveIana(start.TzId),
            allDay ? DateOnly.FromDateTime(start.Value) : null,
            allDay ? LastDay(start, end) : null,
            Repeat(master, start),
            [.. master.Alarms.Where(IcsComposer.IsStartReminder)
                .Select(IcsComposer.MinutesBefore).Distinct().Order()],
            Upper(master.Status) == Tentative ? Availability.Tentative
                : Upper(master.Transparency) == Transparent ? Availability.Free
                : Availability.Busy,
            Upper(master.Class) is Private or Confidential ? Visibility.Private : Visibility.Default,
            master.Url?.ToString());
    }

    /// <summary>
    /// Whether the rule the editor would show says the whole of the stored one. What
    /// <see cref="Repeat"/> keeps is a subset of RFC 5545 § 3.3.10, so a BYHOUR, a second BYMONTH,
    /// an ordinal inside a BYDAY or a WKST would be dropped by a save: recomposing the subset and
    /// comparing it part by part is the only honest way to know. No rule at all is exact.
    /// </summary>
    internal static bool RepeatIsExact(IcsCalendar parsed)
    {
        var master = IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).FirstOrDefault();
        if (master?.RecurrenceRule is not { } rule || master.DtStart is not { } start) return true;
        if (!EditorFrequencies.Contains(rule.Frequency) || Repeat(master, start) is not { } repeat) return false;

        return SameRule(rule, IcsComposer.RuleOf(repeat, start), start);
    }

    /// <summary>The alarms the editor's bell cannot show, each as the sentence it would take to
    /// describe one — so a save can warn that they are there rather than silently keep them.</summary>
    internal static IReadOnlyList<string> ForeignAlarms(IcsCalendar parsed)
    {
        var master = IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).FirstOrDefault();
        return master is null ? []
            : [.. master.Alarms.Where(a => !IcsComposer.IsStartReminder(a)).Select(a => Describe(a, parsed))];
    }

    /// <summary>UNTIL alone is compared by the day it names rather than by its instant: every
    /// client writes it as a UTC date-time carrying the start's own time of day, which the editor's
    /// last-day picker cannot spell and does not need to. Every other part is compared exactly.</summary>
    private static bool SameRule(RecurrenceRule a, RecurrenceRule b, CalDateTime start) =>
        a.Frequency == b.Frequency && a.Interval == b.Interval && a.Count == b.Count
        && LastDayOf(a.Until, start) == LastDayOf(b.Until, start) && a.FirstDayOfWeek == b.FirstDayOfWeek
        && Days(a).SequenceEqual(Days(b), StringComparer.Ordinal)
        && Same(a.ByMonth, b.ByMonth) && Same(a.ByMonthDay, b.ByMonthDay) && Same(a.BySetPosition, b.BySetPosition)
        && Same(a.ByHour, b.ByHour) && Same(a.ByMinute, b.ByMinute) && Same(a.BySecond, b.BySecond)
        && Same(a.ByYearDay, b.ByYearDay) && Same(a.ByWeekNo, b.ByWeekNo);

    /// <summary>A BYDAY entry says a weekday and, sometimes, which one of them the period holds;
    /// the ordinal is part of the comparison because the editor's subset cannot carry it.</summary>
    private static IEnumerable<string> Days(RecurrenceRule rule) =>
        (rule.ByDay ?? []).Select(d => $"{d.Offset}{d.DayOfWeek}").Order(StringComparer.Ordinal);

    private static bool Same(IList<int>? a, IList<int>? b) => (a ?? []).Order().SequenceEqual((b ?? []).Order());

    private static DateOnly? LastDayOf(CalDateTime? until, CalDateTime start) =>
        until is null ? null : UntilDay(until, start);

    private static string Describe(Alarm alarm, IcsCalendar parsed)
    {
        var action = Upper(alarm.Action) ?? UnnamedAction;
        return alarm.Trigger is { } trigger ? $"{action}, {When(trigger, parsed)}" : action;
    }

    /// <summary>When an alarm fires, said the way the editor would have to say it: a distance from
    /// the start — or from the end, which RELATED=END names — or the instant it pins. No distance
    /// at all is the anchor itself, never "0 minutes before".</summary>
    private static string When(Trigger trigger, IcsCalendar parsed)
    {
        if (trigger is { IsRelative: false, DateTime: { } at }) return Pinned(at, parsed);
        if (trigger.Duration is not { } duration) return "at an unreadable moment";

        var span = duration.ToTimeSpanUnspecified();
        var atEnd = trigger.Related?.Equals(EndAnchor, StringComparison.OrdinalIgnoreCase) == true;
        return span == TimeSpan.Zero
            ? atEnd ? "at the end" : "at the start"
            : $"{Distance(span.Duration())} {(span < TimeSpan.Zero ? "before" : "after")}{(atEnd ? " the end" : string.Empty)}";
    }

    /// <summary>RFC 5545 § 3.8.6.3 wants an absolute trigger in UTC, but a client may name a zone
    /// instead: that one is placed through the same engine every other moment goes through, so the
    /// hour shown is always the hour meant. A floating reading belongs to no zone and gets no
    /// suffix — saying UTC there would be inventing what the file does not say.</summary>
    private static string Pinned(CalDateTime at, IcsCalendar parsed) =>
        at.IsUtc || at.TzId is { Length: > 0 }
            ? IcsTimeZones.Place(at, IcsTimeZones.Utc, parsed).Utc.ToString(AbsoluteFormat, CultureInfo.InvariantCulture) + " UTC"
            : at.Value.ToString(AbsoluteFormat, CultureInfo.InvariantCulture);

    private static string Distance(TimeSpan span) =>
        span.Ticks % TimeSpan.TicksPerDay == 0 && span >= TimeSpan.FromDays(1) ? Plural(span.Days, "day")
        : span.Ticks % TimeSpan.TicksPerHour == 0 && span >= TimeSpan.FromHours(1) ? Plural((int)span.TotalHours, "hour")
        : Plural((int)span.TotalMinutes, "minute");

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? string.Empty : "s")}";

    /// <summary>DTEND is exclusive; the editor shows the last day included. An end that does not
    /// outlive its start names the start's own day (RFC 5545 § 3.6.1).</summary>
    private static DateOnly LastDay(CalDateTime start, CalDateTime? end)
    {
        var first = DateOnly.FromDateTime(start.Value);
        var after = end is null ? first : DateOnly.FromDateTime(end.Value);
        return after > first ? after.AddDays(-1) : first;
    }

    private static RecurrenceWrite? Repeat(CalendarEvent master, CalDateTime start)
    {
        if (master.RecurrenceRule is not { } rule) return null;

        var end = rule.Count is > 0 ? RecurrenceEnd.Count
            : rule.Until is not null ? RecurrenceEnd.Until
            : RecurrenceEnd.Never;
        var position = rule.BySetPosition is { Count: > 0 } positions ? positions[0] : (int?)null;
        var days = (rule.ByDay ?? []).Select(d => Code(d.DayOfWeek)).ToList();

        return new RecurrenceWrite(
            rule.Frequency.ToString().ToUpperInvariant(),
            Math.Max(1, rule.Interval),
            position is null ? days : [],
            rule.ByMonthDay is { Count: > 0 } monthDays ? monthDays[0] : null,
            position,
            position is null ? null : days.FirstOrDefault(),
            end,
            end == RecurrenceEnd.Count ? rule.Count : null,
            end == RecurrenceEnd.Until ? UntilDay(rule.Until!, start) : null);
    }

    /// <summary>
    /// UNTIL is an instant in UTC when DTSTART names a zone, so the day the editor shows is the day
    /// that instant falls on IN THAT ZONE — read as UTC, a late-evening event loses or gains a day.
    /// </summary>
    private static DateOnly UntilDay(CalDateTime until, CalDateTime start) =>
        DateOnly.FromDateTime(
            until is { HasTime: true, IsUtc: true } && IcsTimeZones.ResolveIana(start.TzId) is { } zone
                ? IcsTimeZones.FromUtc(DateTime.SpecifyKind(until.Value, DateTimeKind.Utc), zone)
                : until.Value);

    private static DateTime Wall(CalDateTime at) =>
        DateTime.SpecifyKind(at.Value, DateTimeKind.Unspecified);

    private static string Code(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };

    private static string? Upper(string? value) => Trimmed(value)?.ToUpperInvariant();

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
