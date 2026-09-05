using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Services.Calendar;

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The single place the event write rules are written, so POST and PUT read one rule instead of
/// two that could drift — the role <see cref="Services.ContactValidator"/> plays for contacts.
/// </summary>
internal static class EventRequestValidator
{
    /// <summary>28 days: far above anything the editor's reminder picker offers, and a bound the
    /// composer's <c>VALARM TRIGGER</c> can always express as whole minutes.</summary>
    internal const int MaxReminderMinutes = 40320;

    /// <summary>RFC 5545 § 3.3.10's own ranges: a day of the month, the position a BYSETPOS picks
    /// out of a year, and the ordinal a BYDAY code may carry. Bounded here so an out-of-range value
    /// is a refusal in the editor's words rather than a rule the engine silently never fires.</summary>
    private const int MaxMonthDay = 31;
    private const int MaxSetPos = 366;
    private const int MaxWeekOrdinal = 53;

    private static readonly string[] KnownFrequencies = ["DAILY", "WEEKLY", "MONTHLY", "YEARLY"];

    private static readonly string[] Weekdays = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    internal static Result<EventWrite> Validate(EventRequest request)
    {
        if (request == null) return Result.Failure<EventWrite>("Request body is required");

        DateTime? start = null, end = null, requestStart = request.Start, requestEnd = request.End;
        string? timeZone = null;
        DateOnly? startDate = null, endDate = null;

        if (request.IsAllDay)
        {
            if (request.StartDate is not { } sd) return Result.Failure<EventWrite>("An all-day event needs startDate");
            if (request.EndDateInclusive is not { } ed) return Result.Failure<EventWrite>("An all-day event needs endDateInclusive");
            if (ed < sd) return Result.Failure<EventWrite>("Start must be before end");
            startDate = sd;
            endDate = ed;
        }
        else
        {
            if (requestStart is not { } s) return Result.Failure<EventWrite>("A dated event needs a start");
            if (requestEnd is not { } e) return Result.Failure<EventWrite>("A dated event needs an end");
            if (string.IsNullOrWhiteSpace(request.TimeZone))
                return Result.Failure<EventWrite>("A dated event needs a time zone");
            if (!IcsTimeZones.IsKnownIana(request.TimeZone.Trim())) return Result.Failure<EventWrite>(IcsTimeZones.UnknownZone);
            if (e <= s) return Result.Failure<EventWrite>("Start must be before end");
            start = s;
            end = e;
            timeZone = request.TimeZone.Trim();
        }

        RecurrenceWrite? repeat = null;
        if (request.Repeat is { } r)
        {
            var recurrence = ValidateRecurrence(r);
            if (recurrence.IsFailure) return Result.Failure<EventWrite>(recurrence.Error);
            repeat = recurrence.Value;
        }

        var reminders = request.ReminderMinutesBefore ?? [];
        foreach (var minutes in reminders)
        {
            if (minutes < 0 || minutes > MaxReminderMinutes)
                return Result.Failure<EventWrite>($"Reminder must be between 0 and {MaxReminderMinutes} minutes");
        }

        return Result.Success(new EventWrite(
            request.CalendarId, Blank(request.Summary), Blank(request.Location), Blank(request.Description),
            request.IsAllDay, start, end, timeZone, startDate, endDate, repeat, reminders,
            request.Availability, request.Visibility, Blank(request.Url)));
    }

    private static Result<RecurrenceWrite> ValidateRecurrence(RecurrenceRequest request)
    {
        var frequency = (request.Frequency ?? string.Empty).Trim().ToUpperInvariant();
        if (!KnownFrequencies.Contains(frequency)) return Result.Failure<RecurrenceWrite>("Unknown repeat frequency");
        if (request.Interval < 1) return Result.Failure<RecurrenceWrite>("Repeat interval must be at least 1");

        if (request.ByMonthDay is { } monthDay && (monthDay == 0 || monthDay is < -MaxMonthDay or > MaxMonthDay))
            return Result.Failure<RecurrenceWrite>($"Repeat: byMonthDay must be between -{MaxMonthDay} and {MaxMonthDay}");
        if (request.BySetPos is { } position && (position == 0 || position is < -MaxSetPos or > MaxSetPos))
            return Result.Failure<RecurrenceWrite>($"Repeat: bySetPos must be between -{MaxSetPos} and {MaxSetPos}");
        if (!IsWeekday(request.BySetPosDay) || (request.ByDay ?? []).Any(code => code is null || !IsWeekday(code)))
            return Result.Failure<RecurrenceWrite>("Repeat: unknown weekday");

        if (request.Count != null && request.Until != null)
            return Result.Failure<RecurrenceWrite>("Repeat: count and until are exclusive");

        switch (request.End)
        {
            case RecurrenceEnd.Count when request.Count is not (> 0):
                return Result.Failure<RecurrenceWrite>("Repeat: count is required");
            case RecurrenceEnd.Until when request.Until is null:
                return Result.Failure<RecurrenceWrite>("Repeat: until is required");
        }

        return Result.Success(new RecurrenceWrite(
            frequency, request.Interval, request.ByDay ?? [], request.ByMonthDay, request.BySetPos,
            request.BySetPosDay, request.End,
            request.End == RecurrenceEnd.Count ? request.Count : null,
            request.End == RecurrenceEnd.Until ? request.Until : null));
    }

    /// <summary>A BYDAY code: two letters, optionally behind the signed ordinal that picks which
    /// one of them the period holds. Null is not a code and is nobody's refusal.</summary>
    private static bool IsWeekday(string? code)
    {
        if (code is null) return true;

        var text = code.Trim().ToUpperInvariant();
        if (text.Length < 2 || !Weekdays.Contains(text[^2..])) return false;

        var ordinal = text[..^2];
        if (ordinal.Length == 0) return true;
        if (ordinal[0] is '+' or '-') ordinal = ordinal[1..];
        return ordinal.Length > 0 && ordinal.All(char.IsAsciiDigit)
               && int.TryParse(ordinal, out var nth) && nth is >= 1 and <= MaxWeekOrdinal;
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
