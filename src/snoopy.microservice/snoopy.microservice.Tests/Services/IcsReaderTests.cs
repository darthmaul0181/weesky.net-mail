using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The two answers the editor needs before it dares show a rule or a bell as its own: whether the
/// subset it can state says the whole of the file's rule, and which alarms it cannot show at all.
/// </summary>
public sealed class IcsReaderTests
{
    private const string Start = "DTSTART;TZID=Europe/Brussels:20260914T090000";
    private const string End = "DTEND;TZID=Europe/Brussels:20260914T100000";

    [Theory]
    [InlineData("FREQ=WEEKLY;BYDAY=MO")]
    [InlineData("FREQ=MONTHLY;BYSETPOS=-1;BYDAY=FR")]  // "the last Friday", the form the editor writes
    [InlineData("FREQ=MONTHLY;BYSETPOS=2;BYDAY=TU")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=15")]
    public void RepeatIsExact_IsTrueForTheFormsTheEditorWrites(string rrule)
    {
        var parsed = IcsDocument.TryLoad(Ics.Rule(rrule))!;

        Assert.True(IcsReader.RepeatIsExact(parsed));
    }

    [Fact]
    public void RepeatIsExact_IsTrueWithoutARule()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(start: Start, end: End))!;

        Assert.True(IcsReader.RepeatIsExact(parsed));
    }

    [Theory]
    [InlineData("FREQ=MONTHLY;BYDAY=2MO")]              // ordinal in BYDAY: the editor writes BYSETPOS
    [InlineData("FREQ=YEARLY;BYMONTH=3,9;BYDAY=-1MO")]  // two months
    [InlineData("FREQ=WEEKLY;BYDAY=MO,WE;WKST=SU")]     // WKST is not carried
    [InlineData("FREQ=WEEKLY;BYDAY=MO;BYHOUR=9,17")]    // an hour the editor never states
    public void RepeatIsExact_IsFalseWhenTheSubsetLosesSomething(string rrule)
    {
        var parsed = IcsDocument.TryLoad(Ics.Rule(rrule))!;

        Assert.False(IcsReader.RepeatIsExact(parsed));
    }

    /// <summary>Google and Apple write UNTIL as a UTC instant carrying the start's own time of day.
    /// The editor states a last day; comparing instants would lock every ordinary bounded series.</summary>
    [Fact]
    public void RepeatIsExact_ComparesUntilByItsDayNotItsInstant()
    {
        var parsed = IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY;BYDAY=MO;UNTIL=20261220T225959Z"))!;

        Assert.True(IcsReader.RepeatIsExact(parsed));
    }

    /// <summary>An all-day series spells UNTIL as a plain date, which the same day-to-day
    /// comparison has to accept.</summary>
    [Fact]
    public void RepeatIsExact_IsTrueForAnAllDaySeriesBoundedByADate()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(
            start: "DTSTART;VALUE=DATE:20260907", end: "DTEND;VALUE=DATE:20260908",
            extra: "RRULE:FREQ=WEEKLY;UNTIL=20261220"))!;

        Assert.True(IcsReader.RepeatIsExact(parsed));
    }

    [Fact]
    public void ForeignAlarms_ListsWhatTheEditorCannotShow()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(start: Start, end: End, extra:
            "BEGIN:VALARM\r\nACTION:EMAIL\r\nTRIGGER:-P1D\r\nSUMMARY:x\r\nDESCRIPTION:x\r\n"
            + "ATTENDEE:mailto:a@b.c\r\nEND:VALARM\r\n"
            + "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260914T070000Z\r\n"
            + "DESCRIPTION:x\r\nEND:VALARM\r\n"
            + "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:x\r\nEND:VALARM"))!;

        Assert.Equal(["EMAIL, 1 day before", "DISPLAY, 2026-09-14 07:00 UTC"], IcsReader.ForeignAlarms(parsed));
    }

    [Fact]
    public void ForeignAlarms_SaysWhichEndAnAlarmHangsOn()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(start: Start, end: End, extra:
            "BEGIN:VALARM\r\nACTION:AUDIO\r\nTRIGGER;RELATED=END:PT2H\r\nEND:VALARM"))!;

        Assert.Equal(["AUDIO, 2 hours after the end"], IcsReader.ForeignAlarms(parsed));
    }

    /// <summary>No distance is the anchor itself; "0 minutes before" says nothing a reader can use.</summary>
    [Fact]
    public void ForeignAlarms_ReadsANullDistanceAsTheAnchorItself()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(start: Start, end: End, extra:
            "BEGIN:VALARM\r\nACTION:AUDIO\r\nTRIGGER:PT0S\r\nEND:VALARM\r\n"
            + "BEGIN:VALARM\r\nACTION:AUDIO\r\nTRIGGER;RELATED=END:PT0S\r\nEND:VALARM"))!;

        Assert.Equal(["AUDIO, at the start", "AUDIO, at the end"], IcsReader.ForeignAlarms(parsed));
    }

    /// <summary>An absolute trigger the file spells in a zone names one instant like any other: it
    /// is placed in UTC rather than printed as a bare wall clock nobody can situate.</summary>
    [Fact]
    public void ForeignAlarms_PlacesAZonedAbsoluteTriggerInUtc()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(start: Start, end: End, extra:
            "BEGIN:VALARM\r\nACTION:AUDIO\r\nTRIGGER;VALUE=DATE-TIME;TZID=Europe/Brussels:20260914T090000\r\n"
            + "END:VALARM"))!;

        Assert.Equal(["AUDIO, 2026-09-14 07:00 UTC"], IcsReader.ForeignAlarms(parsed));
    }

    /// <summary>A floating absolute trigger belongs to no zone: no suffix, because saying UTC there
    /// would invent what the file does not say.</summary>
    [Fact]
    public void ForeignAlarms_LeavesAFloatingAbsoluteTriggerWithoutASuffix()
    {
        var parsed = IcsDocument.TryLoad(Ics.Single(start: Start, end: End, extra:
            "BEGIN:VALARM\r\nACTION:AUDIO\r\nTRIGGER;VALUE=DATE-TIME:20260914T090000\r\nEND:VALARM"))!;

        Assert.Equal(["AUDIO, 2026-09-14 09:00"], IcsReader.ForeignAlarms(parsed));
    }
}
