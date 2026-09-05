using System.Globalization;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IcsProjectorTests
{
    [Fact]
    public void Dated_ProjectsUtcAndZone()
    {
        var p = Project(Ics.Single(start: "DTSTART;TZID=Europe/Brussels:20260907T090000", end: "DTEND;TZID=Europe/Brussels:20260907T100000"));

        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), p.StartsAt);
        Assert.Equal("Europe/Brussels", p.TimeZone);
        Assert.False(p.IsAllDay);
        Assert.False(p.IsRecurring);
        Assert.False(p.UnknownTimeZone);
        Assert.Equal(p.StartsAt, p.FirstOccurrence);
        Assert.Equal(p.EndsAt, p.LastOccurrence);
    }

    [Fact]
    public void AllDay_IsPosedInCalendarZone_EndExclusive()
    {
        var p = Project(Ics.Single(start: "DTSTART;VALUE=DATE:20260907", end: "DTEND;VALUE=DATE:20260908"), "America/New_York");

        Assert.True(p.IsAllDay);
        Assert.Null(p.TimeZone);
        Assert.Equal(new DateTime(2026, 9, 7, 4, 0, 0, DateTimeKind.Utc), p.StartsAt);   // minuit New York
        Assert.Equal(new DateTime(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc), p.EndsAt);
    }

    [Fact]
    public void Floating_IsPosedInCalendarZone() =>
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            Project(Ics.Single(start: "DTSTART:20260907T090000", end: "DTEND:20260907T100000")).StartsAt);

    [Fact]
    public void Utc_IsNamedUtc() =>
        Assert.Equal(IcsTimeZones.Utc, Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: null)).TimeZone);

    [Fact]
    public void NoDtend_DateLastsOneDay_TimeLastsZero()
    {
        Assert.Equal(TimeSpan.FromDays(1), Span(Ics.Single(start: "DTSTART;VALUE=DATE:20260907", end: null)));
        Assert.Equal(TimeSpan.Zero, Span(Ics.Single(start: "DTSTART:20260907T090000Z", end: null)));
    }

    [Fact]
    public void Duration_IsHonoured() =>
        Assert.Equal(TimeSpan.FromMinutes(90), Span(Ics.Single(start: "DTSTART:20260907T090000Z", end: "DURATION:PT1H30M")));

    [Fact]
    public void InfiniteRule_LastIsSentinel_FirstIsDtstart()
    {
        var p = Project(Ics.Rule("FREQ=WEEKLY"));

        Assert.True(p.IsRecurring);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), p.FirstOccurrence);
        Assert.Equal(IcsProjector.NoEnd, p.LastOccurrence);
    }

    [Fact]
    public void Count_And_Until_BoundLast()
    {
        Assert.Equal(new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc), Project(Ics.Rule("FREQ=WEEKLY;COUNT=4")).LastOccurrence); // 7,14,21,28 · fin 10:00 Bruxelles
        Assert.Equal(new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), Project(Ics.Rule("FREQ=WEEKLY;UNTIL=20260921T235959Z")).LastOccurrence);
    }

    [Fact]
    public void Rdate_And_MovedOverride_ExtendLast()
    {
        var p = Project(Ics.Rule("FREQ=WEEKLY;COUNT=2", extra: "RDATE;TZID=Europe/Brussels:20261225T090000"));
        Assert.Equal(new DateTime(2026, 12, 25, 9, 0, 0, DateTimeKind.Utc), p.LastOccurrence);

        var q = Project(Ics.RuleWithOverride("FREQ=WEEKLY;COUNT=2", overrideStart: "20261130T090000"));
        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0, DateTimeKind.Utc), q.LastOccurrence);
    }

    [Fact]
    public void ExceptionsWithoutMaster_ReadFirstException_NotRecurring()
    {
        var p = Project(Ics.Events(("a", "20260914"), ("a", "20260921")));

        Assert.False(p.IsRecurring);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromDays(1) - TimeSpan.FromHours(2), p.LastOccurrence);
    }

    [Fact]
    public void Attendees_ComeFromEveryComponent_WithTheirRecurrenceId()
    {
        var p = Project(Ics.WithAttendees());

        Assert.Contains(p.Attendees, a => a.IsOrganizer && a.Email == "michel@weesky.be" && a.RecurrenceId is null);
        Assert.Contains(p.Attendees, a => a.Email == "lea@example.org" && a.RecurrenceId == "20260914T090000" && a.PartStat == "ACCEPTED");
    }

    [Fact]
    public void StatusTranspClass_AreProjected()
    {
        var p = Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: null, extra: "STATUS:TENTATIVE\r\nTRANSP:TRANSPARENT\r\nCLASS:PRIVATE"));

        Assert.Equal("TENTATIVE", p.Status);
        Assert.Equal("TRANSPARENT", p.Transparency);
        Assert.Equal("PRIVATE", p.Class);
    }

    [Fact]
    public void Transparency_DefaultsToOpaque() =>
        Assert.Equal("OPAQUE", Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: null)).Transparency);

    [Fact]
    public void WindowsTzid_ResolvesThroughMapping_ThenFileZone_ThenFloating()
    {
        Assert.Equal("Europe/Paris", Project(Ics.Single(start: "DTSTART;TZID=Romance Standard Time:20260907T090000", end: null, zone: Ics.WindowsZone("Romance Standard Time"))).TimeZone);

        var byFile = Project(Ics.Single(start: "DTSTART;TZID=Custom/Zone:20260907T090000", end: null, zone: Ics.FixedZone("Custom/Zone", "+0300")));
        Assert.Equal(new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc), byFile.StartsAt);   // le VTIMEZONE du fichier fait foi
        Assert.Null(byFile.TimeZone);
        Assert.True(byFile.UnknownTimeZone);

        var floating = Project(Ics.Single(start: "DTSTART;TZID=Nowhere/Land:20260907T090000", end: null));
        Assert.Null(floating.TimeZone);                                                       // flottant, journalisé
        Assert.True(floating.UnknownTimeZone);
        Assert.Equal(new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc), floating.StartsAt);
    }

    [Fact]
    public void UnexpandableZone_KeepsTheEventVisible_WithTheSentinel()
    {
        var p = Project(Ics.Single(start: "DTSTART;TZID=Custom/Zone:20260907T090000", end: "DTEND;TZID=Custom/Zone:20260907T100000",
            extra: "RRULE:FREQ=WEEKLY;COUNT=4", zone: Ics.FixedZone("Custom/Zone", "+0300")));

        Assert.True(p.IsRecurring);
        Assert.Equal(new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc), p.FirstOccurrence);
        Assert.Equal(IcsProjector.NoEnd, p.LastOccurrence);
    }

    /// <summary>
    /// Ical.Net 5.2.3 kills the process expanding an hourly series across the autumn fall-back —
    /// a stack overflow no catch block sees. This test does not assert a value so much as prove the
    /// projector never asks for that expansion; a run that ends with "test host crashed" is the
    /// failure. The verdict itself belongs to <see cref="IcsGuards.IsWalkable"/>, which is also what
    /// refuses such a resource at the door.
    /// </summary>
    [Theory]
    [InlineData("RRULE:FREQ=HOURLY;COUNT=200")]
    [InlineData("RRULE:FREQ=DAILY;BYHOUR=2,3;COUNT=200")]
    public void SubDailyRuleInAZone_IsNeverWalked(string rule)
    {
        var p = Project(Ics.Single(start: "DTSTART;TZID=Europe/Brussels:20261024T090000",
            end: "DTEND;TZID=Europe/Brussels:20261024T093000", extra: rule));

        Assert.True(p.IsRecurring);
        Assert.Equal(IcsProjector.NoEnd, p.LastOccurrence);
    }

    [Fact]
    public void SubDailyRuleInUtc_IsStillWalked() =>
        Assert.Equal(new DateTime(2026, 10, 24, 12, 30, 0, DateTimeKind.Utc),
            Project(Ics.Single(start: "DTSTART:20261024T090000Z", end: "DTEND:20261024T093000Z",
                extra: "RRULE:FREQ=HOURLY;COUNT=4")).LastOccurrence);

    [Fact]
    public void ByHourRuleInUtc_IsStillWalked() =>
        Assert.Equal(new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc),
            Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: "DTEND:20260907T093000Z",
                extra: "RRULE:FREQ=DAILY;BYHOUR=9,12;COUNT=4")).LastOccurrence);

    /// <summary>
    /// An RDATE before DTSTART is the event's real start, and the degraded paths used to lose it.
    /// The rule here is endless, so the series is not walked at all.
    /// </summary>
    [Fact]
    public void EarlierRdate_IsTheFirstOccurrence_EvenWhenTheSeriesIsNotWalked()
    {
        var p = Project(Ics.Rule("FREQ=WEEKLY", extra: "RDATE;TZID=Europe/Brussels:20260601T090000"));

        Assert.Equal(new DateTime(2026, 6, 1, 7, 0, 0, DateTimeKind.Utc), p.FirstOccurrence);
        Assert.Equal(IcsProjector.NoEnd, p.LastOccurrence);
    }

    [Fact]
    public void EarlierOverride_IsTheFirstOccurrence_WhenTheZoneIsUnknown()
    {
        var p = Project(Ics.Single(start: "DTSTART;TZID=Custom/Zone:20260907T090000", end: null,
                    extra: "RRULE:FREQ=WEEKLY;COUNT=3", zone: Ics.FixedZone("Custom/Zone", "+0300"))
                .Replace("END:VCALENDAR\r\n",
                    "BEGIN:VEVENT\r\nUID:single\r\nDTSTAMP:20260901T080000Z\r\n"
                    + "RECURRENCE-ID;TZID=Custom/Zone:20260914T090000\r\n"
                    + "DTSTART;TZID=Custom/Zone:20260601T090000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n"));

        Assert.Equal(new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc), p.FirstOccurrence);
    }

    /// <summary>
    /// Neither malformation throws in Ical.Net 5.2.3; the point is that <c>Project</c> is documented
    /// total, so the day one of them does, the projector degrades instead of propagating.
    /// </summary>
    [Fact]
    public void MalformedRules_DoNotEscapeTheProjector()
    {
        var brokenZone = Project(Ics.Single(start: "DTSTART;TZID=Custom/Broken:20260907T090000",
            end: null, extra: "RRULE:FREQ=WEEKLY;COUNT=2", zone: Ics.BrokenZone("Custom/Broken")));
        Assert.True(brokenZone.LastOccurrence >= brokenZone.FirstOccurrence);

        var dateUntil = Project(Ics.Rule("FREQ=WEEKLY;UNTIL=20261001"));
        Assert.True(dateUntil.LastOccurrence >= dateUntil.FirstOccurrence);

        var noSelector = Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: null,
            extra: "RRULE:FREQ=YEARLY;BYSETPOS=1;COUNT=3"));
        Assert.True(noSelector.LastOccurrence >= noSelector.FirstOccurrence);
    }

    /// <summary>
    /// The third tier, on a block that actually has two observances: the same wall-clock reading
    /// lands on a different instant in summer and in winter, which a single-offset zone cannot show.
    /// </summary>
    [Theory]
    [InlineData("20260715T120000", "2026-07-15T10:00:00Z")]
    [InlineData("20261215T120000", "2026-12-15T11:00:00Z")]
    public void FileZone_ReadsTheObservanceInForce(string local, string expected)
    {
        var p = Project(Ics.Single(start: "DTSTART;TZID=Custom/Seasonal:" + local, end: null,
            zone: Ics.SeasonalZone("Custom/Seasonal")));

        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), p.StartsAt);
        Assert.True(p.UnknownTimeZone);
    }

    [Fact]
    public void LongText_IsTruncatedNotDropped()
    {
        var p = Project(Ics.Single(start: "DTSTART:20260907T090000Z", end: null,
            extra: "SUMMARY:" + new string('s', 400) + "\r\nLOCATION:" + new string('l', 400)));

        Assert.Equal(255, p.Summary!.Length);
        Assert.Equal(255, p.Location!.Length);
    }

    [Fact]
    public void NoVevent_ProjectsEmptyRatherThanThrowing()
    {
        var p = IcsProjector.Project(IcsDocument.TryLoad(Ics.Todo())!, Ics.Zone);

        Assert.Equal(string.Empty, p.Uid);
        Assert.Empty(p.Attendees);
    }

    private static EventProjection Project(string ics, string zone = "Europe/Brussels") =>
        IcsProjector.Project(IcsDocument.TryLoad(ics)!, zone);

    private static TimeSpan Span(string ics)
    {
        var p = Project(ics);
        return p.EndsAt - p.StartsAt;
    }
}
