using System.Text.RegularExpressions;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using IcsCalendar = Ical.Net.Calendar;
using IcsEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace weesky.Scotty.Microservice.Services.Calendar;

/// <summary>The one file a contact's birthday becomes (spec, décision 5), and what the webmail reads back.</summary>
internal static partial class BirthdayIcs
{
    internal const string ContactIdProperty = "X-SCOTTY-CONTACT-ID";

    /// <summary>Apple's year for a birthday without one; a leap year, so 29 February exists.</summary>
    internal const int YearLessYear = 1604;

    [GeneratedRegex(@"^(\d{4})-?(\d{2})-?(\d{2})")]
    private static partial Regex Dated();

    [GeneratedRegex(@"^--(\d{2})-?(\d{2})$")]
    private static partial Regex YearLess();

    internal static string DavNameOf(Guid contactId) => $"{contactId}.ics";

    internal static string UidOf(Guid contactId) => $"birthday-{contactId}";

    internal static string? Compose(Contact contact, string reminder, string language, DateTime nowUtc)
    {
        if (contact.Kind != ContactKinds.Individual) return null;
        if (BirthOf(contact.Birthday) is not { } birth || NameOf(contact) is not { } name) return null;

        var summary = $"🎂 {name}";
        var evt = new IcsEvent
        {
            Uid = UidOf(contact.Id),
            DtStart = new CalDateTime(birth.Start),
            DtEnd = new CalDateTime(birth.Start.AddDays(1)),
            Summary = summary,
            Transparency = "TRANSPARENT",
            Class = "PRIVATE",
            DtStamp = IcsComposer.Utc(nowUtc),
        };
        evt.RecurrenceRule = birth.Start is { Month: 2, Day: 29 }
            ? new RecurrencePattern(FrequencyType.Yearly) { ByMonth = [2], ByMonthDay = [-1] }
            : new RecurrencePattern(FrequencyType.Yearly);
        if (birth.Year is { } year)
            evt.Description = language == BirthdayLanguages.Fr ? $"Naissance : {year}" : $"Born {year}";
        evt.AddProperty(ContactIdProperty, contact.Id.ToString());
        if (MinutesAfterStart(reminder) is { } minutes)
            evt.Alarms.Add(new Alarm
            {
                Action = AlarmAction.Display, Description = summary,
                Trigger = new Trigger(Duration.FromMinutes(minutes)),
            });

        var calendar = IcsComposer.Envelope();
        calendar.Events.Add(evt);
        return IcsDocument.Serialize(calendar);
    }

    /// <summary>The contact and the birth year a file of ours carries; null on anything else.</summary>
    internal static (Guid ContactId, int? BirthYear)? Identify(IcsCalendar parsed)
    {
        var master = IcsDocument.MasterOf(parsed);
        if (master?.Properties.Get<string>(ContactIdProperty) is not { } raw || !Guid.TryParse(raw, out var id))
            return null;
        var year = master.DtStart?.Year;
        return (id, year is null or YearLessYear ? null : year);
    }

    private static int? MinutesAfterStart(string reminder) => reminder switch
    {
        BirthdayReminders.SameDay => 9 * 60,
        BirthdayReminders.DayBefore => -15 * 60,
        BirthdayReminders.WeekBefore => -(6 * 24 + 15) * 60,
        _ => null,
    };

    private static string? NameOf(Contact c) =>
        new[] { c.DisplayName, $"{c.FirstName} {c.LastName}", c.Nickname, c.Organization }
            .Select(n => n?.Trim())
            .FirstOrDefault(n => !string.IsNullOrEmpty(n));

    private readonly record struct Birth(DateOnly Start, int? Year);

    private static Birth? BirthOf(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (YearLess().Match(value) is { Success: true } yearLess)
            return Day(YearLessYear, yearLess.Groups[1].Value, yearLess.Groups[2].Value) is { } d ? new Birth(d, null) : null;
        if (Dated().Match(value) is { Success: true } dated)
            return Day(int.Parse(dated.Groups[1].Value), dated.Groups[2].Value, dated.Groups[3].Value) is { } d ? new Birth(d, d.Year) : null;
        return null;
    }

    private static DateOnly? Day(int year, string month, string day) =>
        year >= 1 && DateOnly.TryParseExact($"{year:D4}-{month}-{day}", "yyyy-MM-dd", out var d) ? d : null;
}
