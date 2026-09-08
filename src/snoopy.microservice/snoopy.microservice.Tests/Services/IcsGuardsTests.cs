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
    public void AMethodProperty_IsNotACalendarObjectResource()
    {
        // RFC 4791 § 4.1, MUST NOT: METHOD makes the object an iTIP message, not a resource. A
        // stored PUBLISH would be served back to every client as if it were an event of the user's.
        var ics = Ics.Events(("a", null)).Replace("VERSION:2.0", "VERSION:2.0\r\nMETHOD:PUBLISH");

        var problem = IcsGuards.CheckAll(ics, out _);

        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, problem!.Precondition);
    }

    [Fact]
    public void AnIcalendarBodyThatIsInvalid_SaysSo_NotThatItIsNotIcalendar()
    {
        // Arbitration 2 of the report: an unterminated VEVENT is syntactically broken, but it opens
        // as iCalendar all the same — Ical.Net throws on it, refusing is right, but the message that
        // says "not iCalendar text" is false and sends a client looking for the wrong bug.
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\n";

        var problem = IcsGuards.CheckAll(ics, out _);

        Assert.Equal(IcsPrecondition.ValidCalendarData, problem!.Precondition);
        Assert.DoesNotContain("not iCalendar text", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyThatIsNotCalendarTextAtAll_StillSaysSo()
    {
        var problem = IcsGuards.CheckAll("<?xml version=\"1.0\"?><nope/>", out _);

        Assert.Equal(IcsPrecondition.ValidCalendarData, problem!.Precondition);
        Assert.Contains("not iCalendar text", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionsWithoutMaster_Pass() => Assert.Null(Check(Ics.Events(("a", "20260914"), ("a", "20260921"))));

    [Fact]
    public void ATzidWithNoVtimezoneInTheFile_IsNotACalendarObjectResource()
    {
        // RFC 5545 § 3.2.19: "An individual 'VTIMEZONE' calendar component MUST be specified for each
        // unique 'TZID' parameter value specified in the iCalendar object." 5a accepted this, before
        // the server announced calendar-access; announcing it is what promises RFC 5545's MUSTs.
        var problem = IcsGuards.CheckAll(Ics.WeeklyWithoutZone(), out _);

        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, problem!.Precondition);
        // An import refused without naming the zone it lacks is a refusal nobody can act on.
        Assert.Contains(Ics.Zone, problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATzidThatTheFileDefines_IsAccepted()
    {
        // What Thunderbird, DAVx5 and iOS all send. A false refusal here breaks every one of them.
        var ics = Ics.Single("DTSTART;TZID=US/Eastern:20260101T100000", "DTEND;TZID=US/Eastern:20260101T110000",
            zone: Ics.SeasonalZone("US/Eastern"));

        Assert.Null(IcsGuards.CheckAll(ics, out _));
    }

    /// <summary>An Outlook file names its zone in quotes, and the quotes are not part of the id.</summary>
    [Fact]
    public void AQuotedTzidTheFileDefines_IsAccepted()
    {
        var ics = Ics.Single("DTSTART;TZID=\"Canberra, Melbourne, Sydney\":20260101T100000", null,
            zone: Ics.FixedZone("Canberra, Melbourne, Sydney", "+1000"));

        Assert.Null(IcsGuards.CheckAll(ics, out _));
    }

    [Fact]
    public void AUtcOrFloatingStart_NeedsNoVtimezone()
    {
        Assert.Null(IcsGuards.CheckAll(Ics.Single("DTSTART:20260101T100000Z", null), out _));
        Assert.Null(IcsGuards.CheckAll(Ics.Single("DTSTART:20260101T100000", null), out _));
    }

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
    public void CheckAll_RefusesAVeventWithoutDtstart_AsValidCalendarData()
    {
        // RFC 5545 § 3.6.1: DTSTART is required of a VEVENT without METHOD. None of the four other
        // guards reads it, and a resource admitted without one would be projected at NoInstant —
        // visible from no window, no query and no screen.
        var problem = IcsGuards.CheckAll(NoStart(), out var parsed);

        Assert.Equal(IcsPrecondition.ValidCalendarData, problem!.Precondition);
        Assert.Equal(IcsGuards.NoStart, problem.Message);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void CheckStart_IsTheFifthGuardAndTheOnlyOneReadingDtstart()
    {
        Assert.Equal(IcsPrecondition.ValidCalendarData,
            IcsGuards.CheckStart(IcsDocument.TryLoad(NoStart())!)!.Precondition);
        Assert.Null(IcsGuards.CheckStart(IcsDocument.TryLoad(Ics.Events(("a", null)))!));
        // The four guards before it let the same resource through: the fifth is not redundant.
        Assert.Null(Check(NoStart()));
        Assert.Null(IcsGuards.CheckDensity(IcsDocument.TryLoad(NoStart())!));
        Assert.Null(IcsGuards.CheckExpansion(IcsDocument.TryLoad(NoStart())!));
    }

    [Fact]
    public void CheckAll_JudgesTheSizeBeforeParsing()
    {
        var problem = IcsGuards.CheckAll(Ics.Padded(IcsGuards.MaxIcsBytes + 1), out var parsed);

        // Parsing is the work an oversized body is trying to make us do: no model comes back.
        Assert.Equal(IcsPrecondition.MaxResourceSize, problem!.Precondition);
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("VERSION:1.0", IcsPrecondition.SupportedCalendarData)]
    [InlineData("VERSION:2.0", null)]
    public void CheckAll_RunsTheFourGuardsBeforeTheStart(string version, IcsPrecondition? expected)
    {
        var problem = IcsGuards.CheckAll(Ics.Events(("a", null)).Replace("VERSION:2.0", version), out var parsed);

        Assert.Equal(expected, problem?.Precondition);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void CheckAll_RefusesADensityBomb_AsMaxInstances() =>
        Assert.Equal(IcsPrecondition.MaxInstances, IcsGuards.CheckAll(Ics.DensityBomb(), out _)!.Precondition);

    private static string NoStart() =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n"
        + "BEGIN:VEVENT\r\nUID:nostart\r\nDTSTAMP:20260901T080000Z\r\nSUMMARY:No start\r\nEND:VEVENT\r\n"
        + "END:VCALENDAR\r\n";

    [Theory]
    [InlineData(@"DESCRIPTION:Bad \""escaping\"" here.")]
    [InlineData(@"SUMMARY:a\qb")]
    [InlineData(@"LOCATION:trailing\")]
    public void AnInvalidTextEscape_IsInvalidCalendarData(string line) =>
        Assert.Equal(IcsPrecondition.ValidCalendarData,
            IcsGuards.CheckAll(Ics.Single("DTSTART:20260101T100000Z", null, extra: line), out _)!.Precondition);

    [Theory]
    [InlineData(@"DESCRIPTION:one\, two\; three\\four\nfive\Nsix")]
    [InlineData(@"URL:https://weesky.net/a\b")]           // not a TEXT property: no escaping rules
    [InlineData(@"X-WR-NOTE:c:\temp")]                    // an X- property is not judged either
    public void AValidEscapeOrANonTextProperty_IsAccepted(string line) =>
        Assert.Null(IcsGuards.CheckAll(Ics.Single("DTSTART:20260101T100000Z", null, extra: line), out _));

    [Fact]
    public void ARecurrenceIdNoInstanceOfTheRuleProduces_IsInvalidCalendarData()
    {
        // RFC 5545 3.8.4.4: a RECURRENCE-ID identifies an instance the master's rule generates. One
        // naming 16:00 of a series that only ever fires at 09:00 overrides nothing, for ever.
        var ics = Ics.RuleWithOverrideInUtc("FREQ=WEEKLY", "20260911T160000Z");

        var problem = IcsGuards.CheckAll(ics, out _);

        Assert.Equal(IcsPrecondition.ValidCalendarData, problem!.Precondition);
        Assert.Contains("20260911T160000Z", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecurrenceIdTheRuleProduces_IsAccepted() =>
        Assert.Null(IcsGuards.CheckAll(Ics.RuleWithOverrideInUtc("FREQ=WEEKLY", "20260914T090000Z"), out _));

    /// <summary>The series is walked without its own overrides: attached, the library substitutes
    /// the moved instance for the one the rule generates and the guard could never fire.</summary>
    [Fact]
    public void AnOverrideIsJudgedAgainstTheRuleAlone_NotAgainstItself() =>
        Assert.Equal(IcsPrecondition.ValidCalendarData,
            IcsGuards.CheckAll(Ics.RuleWithOverrideInUtc("FREQ=WEEKLY;COUNT=3", "20261005T090000Z"), out _)!.Precondition);

    [Fact]
    public void ADetachedOverrideWithNoMaster_IsAccepted() =>
        // No rule to judge against. Every client writes these when a series is split.
        Assert.Null(IcsGuards.CheckAll(
            Ics.Single("DTSTART:20260919T100000Z", null, extra: "RECURRENCE-ID:20260919T100000Z"), out _));

    [Fact]
    public void AnOverrideOfAnEventThatDoesNotRepeat_IsAccepted() =>
        Assert.Null(IcsGuards.CheckAll(Ics.Events(("a", null), ("a", "20260914")), out _));

    [Fact]
    public void AnOverrideOfASeriesTheEngineCannotWalk_IsNeverJudged()
    {
        // A guard that has to walk in order to refuse never refuses what it could not walk: in
        // Ical.Net 5.2.3 this series is a stack overflow, which no catch block sees.
        var problem = IcsGuards.CheckAll(Ics.ZonedRuleWithOverride("FREQ=HOURLY", "20260914T110000"), out _);

        Assert.Equal(IcsPrecondition.MaxInstances, problem!.Precondition);
    }

    [Fact]
    public void Problem_NamesWhatItRefused() =>
        Assert.Contains("VTODO", Check(Ics.Todo())!.Message, StringComparison.Ordinal);

    private static IcsProblem? Check(string ics) => IcsGuards.Check(ics, IcsDocument.TryLoad(ics));
}
