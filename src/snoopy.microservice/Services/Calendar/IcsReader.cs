using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
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
