using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>The expanded form of RFC 4791 § 9.6.5, judged on the text a client receives and on
/// the model it reloads: one VEVENT per instance of the window, everything in UTC, no series
/// line, no zone block.</summary>
public sealed class ExpandedCalendarDataTests
{
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AWeeklySeries_ExpandsToOneVeventPerInstance_EachInUtc()
    {
        var text = Expand(Ics.Rule("FREQ=WEEKLY;COUNT=3"));

        var events = Reload(text).Events;
        Assert.Equal(3, events.Count);
        // 09:00 Brussels in September is 07:00 UTC: the instant, never the wall clock re-labelled.
        Assert.Equal(["20260907T070000Z", "20260914T070000Z", "20260921T070000Z"],
            events.Select(e => IcsDocument.LiteralOf(e.RecurrenceIdentifier!.StartTime!)));
        Assert.Equal(["20260907T070000Z", "20260914T070000Z", "20260921T070000Z"],
            events.Select(e => IcsDocument.LiteralOf(e.DtStart!)));
        Assert.Equal(["20260907T080000Z", "20260914T080000Z", "20260921T080000Z"],
            events.Select(e => IcsDocument.LiteralOf(e.DtEnd!)));
        Assert.All(events, e => Assert.Equal("rule", e.Uid));
        Assert.DoesNotContain("RRULE", text);
        Assert.DoesNotContain("TZID=", text);
        Assert.Contains("RECURRENCE-ID:20260914T070000Z", text);
    }

    [Fact]
    public void TheSeriesLinesAndTheZoneBlock_AreLeftOut()
    {
        var text = Expand(Ics.FromPhone());

        var reloaded = Reload(text);
        Assert.Empty(reloaded.TimeZones);
        Assert.DoesNotContain("BEGIN:VTIMEZONE", text);
        Assert.All(reloaded.Events, e =>
        {
            Assert.Null(e.RecurrenceRule);
            Assert.Empty(e.RecurrenceDates.GetAllDates());
            Assert.Empty(e.ExceptionDates.GetAllDates());
        });
    }

    [Fact]
    public void AnOverride_ReplacesTheInstanceItNames_AndNamesTheSlotNotTheMovedTime()
    {
        // The 7th, the 14th moved to 11:00, the 21st removed by EXDATE, the 28th.
        var events = Reload(Expand(Ics.WeeklyWithExdateAndOverride())).Events;

        Assert.Equal(["20260907T070000Z", "20260914T070000Z", "20260928T070000Z"],
            events.Select(e => IcsDocument.LiteralOf(e.RecurrenceIdentifier!.StartTime!)));
        var moved = events.Single(e => e.Summary == "Standup (moved)");
        // The RFC's own example: RECURRENCE-ID is the original slot, DTSTART where it went.
        Assert.Equal("20260914T070000Z", IcsDocument.LiteralOf(moved.RecurrenceIdentifier!.StartTime!));
        Assert.Equal("20260914T090000Z", IcsDocument.LiteralOf(moved.DtStart!));
        Assert.DoesNotContain(events, e => IcsDocument.LiteralOf(e.DtStart!) == "20260914T070000Z");
    }

    [Fact]
    public void AnAllDaySeries_StaysInDates_RecurrenceIdIncluded()
    {
        var text = Expand(Ics.AllDayWeekly());

        // A DATE has no instant to put in UTC: the day is the day, for every reader (sabre does
        // the same).
        var events = Reload(text).Events;
        Assert.Equal(["20260907", "20260914", "20260921", "20260928"],
            events.Select(e => IcsDocument.LiteralOf(e.RecurrenceIdentifier!.StartTime!)));
        Assert.All(events, e => Assert.False(e.RecurrenceIdentifier!.StartTime!.HasTime));
        Assert.All(events, e => Assert.False(e.DtStart!.HasTime));
        Assert.Equal(["20260908", "20260915", "20260922", "20260929"],
            events.Select(e => IcsDocument.LiteralOf(e.DtEnd!)));
        Assert.Contains("RECURRENCE-ID;VALUE=DATE:20260914", text);
    }

    [Fact]
    public void TheMastersAlarms_RideIntoEveryGeneratedInstance()
    {
        var events = Reload(Expand(Ics.FromPhone())).Events;

        var generated = events.Where(e => e.Summary == "Standup").ToList();
        Assert.Equal(3, generated.Count);
        Assert.All(generated, e => Assert.Equal(2, e.Alarms.Count));
        Assert.All(generated, e => Assert.Contains(e.Alarms, a => a.Action == "DISPLAY"));
    }

    [Fact]
    public void AFloatingSeries_IsPosedInTheCalendarsZone()
    {
        var floating = Ics.Single("DTSTART:20260907T090000", "DTEND:20260907T100000",
            extra: "RRULE:FREQ=DAILY;COUNT=2");

        var brussels = Reload(Expand(floating, Ics.Zone)).Events;
        var utc = Reload(Expand(floating, IcsTimeZones.Utc)).Events;

        // No zone in the file, so the calendar's says what 09:00 means — décision 5 of 5a.
        Assert.Equal(["20260907T070000Z", "20260908T070000Z"],
            brussels.Select(e => IcsDocument.LiteralOf(e.DtStart!)));
        Assert.Equal(["20260907T070000Z", "20260908T070000Z"],
            brussels.Select(e => IcsDocument.LiteralOf(e.RecurrenceIdentifier!.StartTime!)));
        Assert.Equal(["20260907T090000Z", "20260908T090000Z"],
            utc.Select(e => IcsDocument.LiteralOf(e.DtStart!)));
    }

    [Fact]
    public void ADuration_BecomesAnEndInUtc()
    {
        var events = Reload(Expand(Ics.Single("DTSTART:20260907T090000Z", "DURATION:PT30M",
            extra: "RRULE:FREQ=DAILY;COUNT=2"))).Events;

        Assert.Equal(["20260907T093000Z", "20260908T093000Z"],
            events.Select(e => IcsDocument.LiteralOf(e.DtEnd!)));
        Assert.All(events, e => Assert.Null(e.Properties.Get<object>("DURATION")));
    }

    [Fact]
    public void AnEventWithNoEnd_GetsNone()
    {
        var events = Reload(Expand(Ics.Single("DTSTART:20260907T090000Z", null,
            extra: "RRULE:FREQ=DAILY;COUNT=2"))).Events;

        // An end equal to the start would be a DTEND RFC 5545 § 3.8.2.2 forbids.
        Assert.All(events, e => Assert.Null(e.DtEnd));
    }

    [Fact]
    public void ANonRecurringEvent_IsServedInUtcWithoutARecurrenceId()
    {
        var single = Reload(Expand(Ics.Single(
            "DTSTART;TZID=" + Ics.Zone + ":20260907T090000",
            "DTEND;TZID=" + Ics.Zone + ":20260907T100000"))).Events.Single();

        // Not an instance of anything: a RECURRENCE-ID on it would make it an override without a
        // master to every client that keys on the pair (sabre writes none either).
        Assert.Null(single.RecurrenceIdentifier);
        Assert.Equal("20260907T070000Z", IcsDocument.LiteralOf(single.DtStart!));
        Assert.Equal("20260907T080000Z", IcsDocument.LiteralOf(single.DtEnd!));
    }

    [Fact]
    public void OnlyTheInstancesOfTheWindow_AreServed()
    {
        var events = Reload(Expand(Ics.Rule("FREQ=WEEKLY;COUNT=3"),
            from: new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc))).Events;

        Assert.Equal("20260914T070000Z",
            IcsDocument.LiteralOf(Assert.Single(events).RecurrenceIdentifier!.StartTime!));
    }

    [Fact]
    public void PastTheCap_ItRefusesWithMaxInstances_NeverATruncatedAnswer()
    {
        // One instance a minute over eight days is past ten thousand: the walk would stop at the
        // ceiling, and a document quietly missing the rest is what the refusal exists to prevent.
        var from = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);
        var refused = Assert.Throws<DavPreconditionException>(
            () => Expand(Ics.DensityBomb(), from: from, to: from.AddDays(8)));

        Assert.Equal(CalDavError.MaxInstances, refused.Condition);
    }

    [Fact]
    public void UnderTheCap_TheSameSeriesIsServed()
    {
        var from = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);

        var events = Reload(Expand(Ics.DensityBomb(), from: from, to: from.AddHours(1))).Events;

        Assert.Equal(60, events.Count);
    }

    [Fact]
    public void TheCap_IsTheExpandersOwn()
    {
        // The one object both the walk and the report read: ten thousand per year of window
        // (a fraction counting as a whole year), plus the one that proves the ceiling was hit.
        Assert.Equal(IcsGuards.MaxInstancesPerYear + 1, OccurrenceExpander.CapFor(From, From.AddDays(1)));
        Assert.Equal(IcsGuards.MaxInstancesPerYear + 1, OccurrenceExpander.CapFor(From, From.AddDays(365)));
        Assert.Equal(2 * IcsGuards.MaxInstancesPerYear + 1, OccurrenceExpander.CapFor(From, From.AddDays(400)));
    }

    [Fact]
    public void AFileThatDoesNotParse_IsRefusedAsValidCalendarData()
    {
        var refused = Assert.Throws<DavPreconditionException>(() => Expand("not a calendar"));

        Assert.Equal(CalDavError.ValidCalendarData, refused.Condition);
    }

    [Fact]
    public void TheEnvelope_IsACalendarOfItsOwn()
    {
        var reloaded = Reload(Expand(Ics.Rule("FREQ=WEEKLY;COUNT=1")));

        Assert.Equal("2.0", reloaded.Version);
        Assert.NotEmpty(reloaded.ProductId ?? string.Empty);
    }

    private static string Expand(string ics, string zone = Ics.Zone, DateTime? from = null, DateTime? to = null) =>
        ExpandedCalendarData.Expand(ics, from ?? From, to ?? To, zone);

    private static IcsCalendar Reload(string text)
    {
        var reloaded = IcsDocument.TryLoad(text);
        Assert.NotNull(reloaded);
        return reloaded;
    }
}
