using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IcsGuardsTests
{
    [Fact]
    public void Unparsable_IsValidCalendarData() =>
        Assert.Equal(IcsPrecondition.ValidCalendarData,
            IcsGuards.Check("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n", null)!.Precondition);

    [Fact]
    public void TwoUids_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, Check(Ics.Events(("a", null), ("b", null)))!.Precondition);

    [Fact]
    public void TwoMasters_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, Check(Ics.Events(("a", null), ("a", null)))!.Precondition);

    [Fact]
    public void MissingUid_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, Check(Ics.Events(("", null)))!.Precondition);

    [Fact]
    public void Vtodo_IsSupportedCalendarComponent() =>
        Assert.Equal(IcsPrecondition.SupportedCalendarComponent, Check(Ics.Todo())!.Precondition);

    /// <summary>A collection holding nothing this module stores names the component it holds;
    /// a resource mixing one into its VEVENT is not a resource at all. Two different refusals.</summary>
    [Fact]
    public void Vfreebusy_Alone_IsSupportedCalendarComponent() =>
        Assert.Equal(IcsPrecondition.SupportedCalendarComponent, Check(FreeBusyOnly())!.Precondition);

    [Fact]
    public void VtodoBesideAVevent_IsValidCalendarObjectResource() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource,
            Check(Ics.Events(("a", null)).Replace("END:VCALENDAR",
                "BEGIN:VTODO\r\nUID:a\r\nDTSTAMP:20260901T080000Z\r\nSUMMARY:Buy milk\r\nEND:VTODO\r\nEND:VCALENDAR"))!.Precondition);

    /// <summary>
    /// A VALARM carries a UID of its own — Google writes one, iOS writes it beside X-WR-ALARMUID —
    /// and it answers for no component: counted, it would let a UID-less VEVENT through the gate.
    /// </summary>
    [Fact]
    public void AnAlarmsOwnUid_DoesNotStandInForItsComponents() =>
        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource,
            Check(Ics.EventWithoutUid().Replace("SUMMARY:Anonymous\r\n",
                "SUMMARY:Anonymous\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\n"
                + "X-WR-ALARMUID:0A1B2C3D\r\nUID:0A1B2C3D\r\nDESCRIPTION:x\r\nEND:VALARM\r\n"))!.Precondition);

    [Fact]
    public void AnAlarmsOwnUid_DoesNotHideAMissingOne_WhenTheComponentHasOne() =>
        Assert.Null(Check(Ics.FromPhone()));

    /// <summary>The two values the DDL cannot cut without changing who the resource is or who it
    /// addresses: <c>uid VARCHAR(255)</c> and <c>calendar_attendees.email VARCHAR(320)</c>.</summary>
    [Fact]
    public void AUidOverTwoHundredAndFiftyFive_IsValidCalendarData() =>
        Assert.Equal(IcsPrecondition.ValidCalendarData,
            Check(Ics.Events((new string('u', 256), null)))!.Precondition);

    [Fact]
    public void AnAttendeeAddressOverThreeHundredAndTwenty_IsValidCalendarData()
    {
        var address = new string('a', 315) + "@x.org";

        Assert.Equal(IcsPrecondition.ValidCalendarData,
            Check(Ics.Events(("a", null)).Replace("SUMMARY:Day\r\n",
                "SUMMARY:Day\r\nATTENDEE:mailto:" + address + "\r\n"))!.Precondition);
    }

    [Fact]
    public void TheEdgesOfBothWidths_Pass()
    {
        Assert.Null(Check(Ics.Events((new string('u', 255), null))));
        Assert.Null(Check(Ics.Events(("a", null)).Replace("SUMMARY:Day\r\n",
            "SUMMARY:Day\r\nATTENDEE:mailto:" + new string('a', 314) + "@x.org\r\n")));
    }

    /// <summary>
    /// Décision 4, tried rather than counted. The one rule a real file brings that Ical.Net used to
    /// throw on — a TZID only the file's own VTIMEZONE defines — is admitted, because the walk it
    /// tries is the detached one the expander uses.
    /// </summary>
    [Fact]
    public void CheckExpansion_AdmitsARuleInAThirdTierZone()
    {
        var resource = IcsResources.Split(File.ReadAllText(IcsResourcesTests.Corpus("outlook-2003.ics"))).Resources.Single();

        Assert.Null(IcsGuards.CheckExpansion(IcsDocument.TryLoad(resource)!));
        Assert.Null(IcsGuards.CheckExpansion(IcsDocument.TryLoad(Ics.Rule("FREQ=WEEKLY"))!));
    }

    /// <summary>A series the walkability guard refuses is never handed to the library — in
    /// Ical.Net 5.2.3 that is a stack overflow, which no catch block sees.</summary>
    [Fact]
    public void CheckExpansion_NeverWalksWhatIsWalkabilityRefuses() =>
        Assert.Null(IcsGuards.CheckExpansion(IcsDocument.TryLoad(Ics.Rule("FREQ=HOURLY"))!));

    private static string FreeBusyOnly() =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + "BEGIN:VFREEBUSY\r\nUID:busy\r\nDTSTAMP:20260901T080000Z\r\n"
        + "DTSTART:20260907T090000Z\r\nDTEND:20260907T100000Z\r\nEND:VFREEBUSY\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void WrongVersion_IsSupportedCalendarData() =>
        Assert.Equal(IcsPrecondition.SupportedCalendarData,
            Check(Ics.Events(("a", null)).Replace("VERSION:2.0", "VERSION:1.0"))!.Precondition);

    [Fact]
    public void ExceptionsWithoutMaster_Pass() => Assert.Null(Check(Ics.Events(("a", "20260914"), ("a", "20260921"))));

    [Fact]
    public void MissingVtimezone_Passes() => Assert.Null(Check(Ics.WeeklyWithoutZone()));

    [Fact]
    public void OverOneMegabyte_IsMaxResourceSize() =>
        Assert.Equal(IcsPrecondition.MaxResourceSize,
            IcsGuards.Check(Ics.Padded(IcsGuards.MaxIcsBytes + 1), null)!.Precondition);

    [Fact]
    public void ExactlyOneMegabyte_Passes() => Assert.Null(Check(Ics.Padded(IcsGuards.MaxIcsBytes)));

    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=1,15")]
    public void Density_AcceptsAYearAnAgendaCouldHold(string rrule) =>
        Assert.Null(IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.Rule(rrule))!));

    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=MINUTELY")]
    [InlineData("FREQ=SECONDLY;COUNT=1000000")]
    [InlineData("FREQ=DAILY;BYHOUR=0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23")]
    public void Density_IsJudgedOnOneYear(string rrule) =>
        Assert.Equal(IcsPrecondition.MaxInstances,
            IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.Rule(rrule))!)!.Precondition);

    /// <summary>
    /// The BY* parts are what the count was blind to: this rule fires every minute of every day
    /// while its FREQ token says "once a day".
    /// </summary>
    [Fact]
    public void Density_CountsWhatTheByPartsExpand()
    {
        var rrule = "FREQ=DAILY;BYHOUR=" + string.Join(',', Enumerable.Range(0, 24))
                    + ";BYMINUTE=" + string.Join(',', Enumerable.Range(0, 60));

        Assert.Equal(IcsPrecondition.MaxInstances,
            IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.RuleInUtc(rrule))!)!.Precondition);
    }

    /// <summary>
    /// Ruling over décision 4's "un horaire 8 760": a rule repeating more than once a day is
    /// refused outright when DTSTART names a zone, because Ical.Net cannot expand it without
    /// killing the process. In UTC the same rule is admitted.
    /// </summary>
    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=DAILY;BYHOUR=9,14")]
    public void Density_RefusesSubDailyRepetitionInAZone(string rrule)
    {
        var problem = IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.Rule(rrule))!);

        Assert.Equal(IcsPrecondition.MaxInstances, problem!.Precondition);
        Assert.Contains("Europe/Brussels", problem.Message, StringComparison.Ordinal);
        Assert.False(IcsGuards.IsWalkable(IcsDocument.TryLoad(Ics.Rule(rrule))!));
    }

    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("FREQ=DAILY;BYHOUR=9,14")]
    public void Density_AdmitsTheSameRuleInUtc(string rrule)
    {
        Assert.Null(IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.RuleInUtc(rrule))!));
        Assert.True(IcsGuards.IsWalkable(IcsDocument.TryLoad(Ics.RuleInUtc(rrule))!));
    }

    /// <summary>
    /// The eight BY* parts a YEARLY rule expands, each 256 entries long: 256^8 is exactly 2^64, so
    /// an unsaturated product wraps to zero and the rule is counted as no instances at all. The
    /// lists are legal values repeated — a few kilobytes, nothing an attacker would strain to send.
    /// </summary>
    [Fact]
    public void Density_CannotBeWrappedPastTheCeiling()
    {
        var rrule = "FREQ=YEARLY;BYSECOND=" + Repeated(Enumerable.Range(0, 60))
                    + ";BYMINUTE=" + Repeated(Enumerable.Range(0, 60))
                    + ";BYHOUR=" + Repeated(Enumerable.Range(0, 24))
                    + ";BYDAY=" + Repeated(["MO", "TU", "WE", "TH", "FR", "SA", "SU"])
                    + ";BYMONTHDAY=" + Repeated(Enumerable.Range(1, 28))
                    + ";BYYEARDAY=" + Repeated(Enumerable.Range(1, 366))
                    + ";BYWEEKNO=" + Repeated(Enumerable.Range(1, 53))
                    + ";BYMONTH=" + Repeated(Enumerable.Range(1, 12));

        Assert.Equal(IcsPrecondition.MaxInstances,
            IcsGuards.CheckDensity(IcsDocument.TryLoad(Ics.RuleInUtc(rrule))!)!.Precondition);
    }

    private const int WrappingListLength = 256;

    private static string Repeated<T>(IEnumerable<T> values)
    {
        var pool = values.ToList();
        return string.Join(',', Enumerable.Range(0, WrappingListLength).Select(i => pool[i % pool.Count]));
    }

    [Fact]
    public void CheckSize_JudgesTheBodyWithoutParsingIt()
    {
        Assert.Equal(IcsPrecondition.MaxResourceSize, IcsGuards.CheckSize(Ics.Padded(IcsGuards.MaxIcsBytes + 1))!.Precondition);
        Assert.Null(IcsGuards.CheckSize(Ics.Padded(IcsGuards.MaxIcsBytes)));
        Assert.Null(IcsGuards.CheckSize("not an icalendar at all"));
    }

    [Fact]
    public void Problem_NamesWhatItRefused() =>
        Assert.Contains("VTODO", Check(Ics.Todo())!.Message, StringComparison.Ordinal);

    private static IcsProblem? Check(string ics) => IcsGuards.Check(ics, IcsDocument.TryLoad(ics));
}
