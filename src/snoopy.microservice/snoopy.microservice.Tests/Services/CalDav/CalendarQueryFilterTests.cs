using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

/// <summary>The calendar-query filter: every line of the refusal grid of spec § 8, the closing of
/// an open bound, and the evaluation on the file's own model — instances, alarms, properties,
/// parameters and collations.</summary>
public sealed class CalendarQueryFilterTests
{
    private const string Auckland = "Pacific/Auckland";

    // ---- the grid of refusals ----------------------------------------------------------------

    [Fact]
    public void AFilterWithoutACompFilter_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter());

    [Fact]
    public void TwoRootCompFilters_AreMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter(Comp("VCALENDAR"), Comp("VCALENDAR")));

    [Fact]
    public void ARootThatIsNotVCalendar_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter(Comp("VEVENT")));

    [Fact]
    public void AVCalendarAlone_IsTheWholeCalendar()
    {
        var spec = CalendarQueryFilter.Parse(Filter(Comp("VCALENDAR")));

        Assert.True(spec.AllEvents);
        Assert.False(spec.NoneMatch);
        Assert.True(CalendarQueryFilter.Matches(Load(Ics.Rule("FREQ=WEEKLY")), spec, Ics.Zone));
    }

    [Fact]
    public void ABareVEventCompFilter_IsEveryEvent_TheThunderbirdShape()
    {
        var spec = CalendarQueryFilter.Parse(VEvent());

        Assert.False(spec.AllEvents);
        Assert.Null(spec.TimeRange);
        Assert.True(CalendarQueryFilter.Matches(Load(Ics.Rule("FREQ=WEEKLY")), spec, Ics.Zone));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsNotDefined_OnTheVEventOrTheVCalendar_MatchesNothing(bool onEvent)
    {
        var filter = onEvent ? VEvent(IsNotDefined()) : Filter(Comp("VCALENDAR", IsNotDefined()));

        var spec = CalendarQueryFilter.Parse(filter);

        Assert.True(spec.NoneMatch);
        Assert.False(CalendarQueryFilter.Matches(Load(Ics.Rule("FREQ=WEEKLY")), spec, Ics.Zone));
    }

    [Fact]
    public void ATimeRangeDirectlyUnderTheVCalendar_IsMalformed() =>
        // § 9.7's grammar puts time-range under a comp-filter, and VCALENDAR is not a component
        // that spans time. errors.xml/15.xml.
        AssertRefused(CalDavError.ValidFilter,
            Filter(Comp("VCALENDAR", TimeRange("20260101T000000Z", "20260201T000000Z"))));

    [Fact]
    public void AVEventNestedInAVEvent_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter(Comp("VCALENDAR", Comp("VEVENT", Comp("VEVENT")))));

    [Fact]
    public void AVAlarmDirectlyUnderTheVCalendar_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter(Comp("VCALENDAR", Comp("VALARM"))));

    [Fact]
    public void ATimeRangeUnderAVTimezone_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter,
            Filter(Comp("VCALENDAR", Comp("VTIMEZONE", TimeRange("20260101T000000Z", null)))));

    [Fact]
    public void AnUnknownComponentName_IsUnsupported_NotMalformed() =>
        // X-COMP is a well-formed reference to a component we do not serve: § 7.8's own division.
        AssertRefused(CalDavError.SupportedFilter, Filter(Comp("VCALENDAR", Comp("X-COMP"))));

    [Fact]
    public void AVTodoCompFilter_IsUnsupported_NotMalformed() =>
        AssertRefused(CalDavError.SupportedFilter, Filter(Comp("VCALENDAR", Comp("VTODO"))));

    [Fact]
    public void TwoVEventCompFilters_AreUnsupported() =>
        AssertRefused(CalDavError.SupportedFilter, Filter(Comp("VCALENDAR", Comp("VEVENT"), Comp("VEVENT"))));

    [Fact]
    public void ATestAnyOf_IsUnsupported_ItIsCardDavsAttribute()
    {
        var filter = VEvent(Prop("SUMMARY", Text("x")));
        filter.SetAttributeValue("test", "anyof");

        // Served as the conjunction it cannot be, the client would file a false result set.
        AssertRefused(CalDavError.SupportedFilter, filter);
    }

    [Fact]
    public void ATestAllOf_IsTheSemanticItAlreadyHas()
    {
        var filter = VEvent(Prop("SUMMARY", Text("Standup")));
        filter.SetAttributeValue("test", "allof");

        Assert.True(CalendarQueryFilter.Matches(Load(Ics.FromPhone()), CalendarQueryFilter.Parse(filter), Ics.Zone));
    }

    [Fact]
    public void APropFilterOnTheVCalendar_IsUnsupported() =>
        AssertRefused(CalDavError.SupportedFilter, Filter(Comp("VCALENDAR", Prop("PRODID", Text("x")))));

    [Fact]
    public void ATimeRangeWithNoBoundAtAll_IsMalformed_NeverAWindowAroundNow() =>
        AssertRefused(CalDavError.ValidFilter, VEvent(TimeRange(null, null)));

    [Theory]
    [InlineData("20260907T100000Z", "20260907T090000Z")]
    [InlineData("20260907T090000Z", "20260907T090000Z")]
    public void ATimeRangeEndingAtOrBeforeItsStart_IsMalformed(string start, string end) =>
        AssertRefused(CalDavError.ValidFilter, VEvent(TimeRange(start, end)));

    [Theory]
    [InlineData("2026-09-07T09:00:00Z")]
    [InlineData("20260907T090000")]
    [InlineData("20260907")]
    [InlineData("20260907T090000+0200")]
    public void ATimeRangeBoundOutsideTheUtcForm_IsMalformed(string start) =>
        AssertRefused(CalDavError.ValidFilter, VEvent(TimeRange(start, null)));

    [Fact]
    public void AMissingEnd_StaysMissing()
    {
        // RFC 4791 § 9.9: « the server assumes unbounded limits in that direction ». Closing at
        // five years answered wrong without saying so — DAVx5 asks this very question at every
        // sync, and reports.xml/time-range t13 measures it.
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange("20260907T090000Z", null)));

        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), spec.TimeRange!.FromUtc);
        Assert.Null(spec.TimeRange.ToUtc);
    }

    [Fact]
    public void AMissingStart_StaysMissing()
    {
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange(null, "20260907T090000Z")));

        Assert.Null(spec.TimeRange!.FromUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), spec.TimeRange.ToUtc);
    }

    [Fact]
    public void AWindowWiderThanTheEnginesSpan_IsMalformed()
    {
        // A window naming BOTH bounds is capped by the window itself, so past MaxSpan nothing
        // would bound what one candidate makes the walk produce. Assumed: the narrower request is
        // the only one refused — an absent bound is served open, and the walk caps itself instead.
        AssertRefused(CalDavError.ValidFilter, VEvent(TimeRange("20260101T000000Z", "20320101T000000Z")));
        Assert.NotNull(CalendarQueryFilter.Parse(VEvent(TimeRange("20260101T000000Z", "20301231T000000Z"))).TimeRange);
    }

    [Fact]
    public void ABoundAtTheEdgeOfTime_NeedsNoClosing_AndNeverThrows()
    {
        // What used to shift MaxSpan past DateTime.MaxValue and be clamped. Nothing is shifted now.
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange("99991231T000000Z", null)));

        Assert.Null(spec.TimeRange!.ToUtc);
        Assert.NotNull(CalendarQueryFilter.Parse(VEvent(TimeRange(null, "00010101T000000Z"))).TimeRange);
    }

    [Fact]
    public void AVAlarmTimeRange_KeepsItsClosure()
    {
        // The alarm walk reads a WHOLE window rather than stopping at a first hit; leaving it open
        // would reread the calendar at every query. Named in the spec as a known non-conformity
        // that no targeted client exercises.
        var spec = CalendarQueryFilter.Parse(VEvent(Comp("VALARM", TimeRange("20260907T090000Z", null))));

        var from = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(from + OccurrenceExpander.MaxSpan, spec.AlarmFilters.Single().TimeRange!.ToUtc);
    }

    [Fact]
    public void AnOpenWindow_NarrowedToOneComponent_WalksAtMostTheEnginesSpan()
    {
        // A prop-filter narrows the walk to one component, and that one cannot stop at a first hit.
        // Read off the window, the cap here would be 100_000_001 instead of 60_001 — a MINUTELY
        // rule walked to the end of time for an answer of false.
        var parsed = Load(Ics.DensityBomb());   // FREQ=MINUTELY, no UNTIL
        var window = TimeRange("20260907T000000Z", null);

        Assert.True(Matches(parsed, VEvent(window)));
        Assert.False(Matches(parsed, VEvent(window, Prop("SUMMARY", Text("Tock")))));
    }

    [Fact]
    public void TwoTimeRanges_AreMalformed() =>
        AssertRefused(CalDavError.ValidFilter,
            VEvent(TimeRange("20260907T090000Z", null), TimeRange("20260908T090000Z", null)));

    [Fact]
    public void ParseTimeRange_DemandsBothBounds_WhenTold_AndAnswersA400WithoutACondition()
    {
        // FreeBusyReport's contract: both bounds, and a bare 400 rather than valid-filter.
        Assert.Throws<DavBadRequestException>(() =>
            CalendarQueryFilter.ParseTimeRange(TimeRange("20260907T090000Z", null), AbsentBound.Refused, null));
        Assert.Throws<DavBadRequestException>(() =>
            CalendarQueryFilter.ParseTimeRange(TimeRange(null, null), AbsentBound.Open, null));

        var both = CalendarQueryFilter.ParseTimeRange(
            TimeRange("20260907T090000Z", "20260908T090000Z"), AbsentBound.Refused, null);
        Assert.Equal(TimeSpan.FromDays(1), both.Closed.To - both.Closed.From);
    }

    [Fact]
    public void UnicodeCasemap_IsRefusedWithTheCalendarsOwnCondition()
    {
        // Not announced on a calendar (RFC 4791 § 7.5.1 imposes the two others) — and the XName is
        // CalDAV's, not the book's.
        AssertRefused(CalDavError.SupportedCollation,
            VEvent(Prop("SUMMARY", Text("x", collation: DavCollation.UnicodeCasemap))));
    }

    [Fact]
    public void AnUnknownMatchType_IsUnsupported() =>
        AssertRefused(CalDavError.SupportedFilter, VEvent(Prop("SUMMARY", Text("x", matchType: "regex"))));

    [Fact]
    public void ANegateConditionOutsideYesNo_IsMalformed()
    {
        var text = Text("x");
        text.SetAttributeValue("negate-condition", "maybe");

        AssertRefused(CalDavError.ValidFilter, VEvent(Prop("SUMMARY", text)));
    }

    [Fact]
    public void AnAlarmFilterCarryingBothIsNotDefinedAndATimeRange_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter,
            VEvent(Comp("VALARM", IsNotDefined(), TimeRange("20260907T000000Z", null))));

    [Fact]
    public void APropFilterInsideAVAlarm_IsUnsupported() =>
        AssertRefused(CalDavError.SupportedFilter, VEvent(Comp("VALARM", Prop("ACTION", Text("DISPLAY")))));

    [Fact]
    public void ATimeRangeNestedInAPropFilter_IsMalformed_TheRfcsOwnExample()
    {
        // RFC 4791 § 7.8.9, word for word: « a CALDAV:filter cannot nest a time-range element in a
        // prop name="SUMMARY" element ». Accepted until now — a 207 over a filter we never applied.
        AssertRefused(CalDavError.ValidFilter,
            VEvent(Prop("SUMMARY", TimeRange("20260101T000000Z", "20260201T000000Z"))));
    }

    [Fact]
    public void TwoTextMatchesOnOneProperty_AreMalformed() =>
        AssertRefused(CalDavError.ValidFilter, VEvent(Prop("SUMMARY", Text("a"), Text("b"))));

    [Fact]
    public void IsNotDefinedBesideATextMatch_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, VEvent(Prop("SUMMARY", IsNotDefined(), Text("a"))));

    [Fact]
    public void APropFilterWithoutAName_IsMalformed()
    {
        var prop = Prop("SUMMARY", Text("a"));
        prop.Attribute("name")!.Remove();

        AssertRefused(CalDavError.ValidFilter, VEvent(prop));
    }

    [Fact]
    public void AParamFilterWithTwoChildren_IsMalformed_AndOneWithAnUnknownChild_IsUnsupported()
    {
        AssertRefused(CalDavError.ValidFilter,
            VEvent(Prop("ATTENDEE", Param("PARTSTAT", IsNotDefined(), Text("x")))));
        AssertRefused(CalDavError.SupportedFilter,
            VEvent(Prop("ATTENDEE", Param("PARTSTAT", Comp("VALARM")))));
    }

    // ---- the time-range, on instances ------------------------------------------------------

    [Fact]
    public void AWeeklySeries_MatchesOnTheOneInstanceTheWindowCovers()
    {
        var parsed = Load(Ics.Rule("FREQ=WEEKLY;COUNT=3"));   // the 7th, 14th, 21st at 07:00Z

        Assert.True(Matches(parsed, VEvent(TimeRange("20260921T000000Z", "20260922T000000Z"))));
        Assert.False(Matches(parsed, VEvent(TimeRange("20260922T000000Z", "20260923T000000Z"))));
        Assert.False(Matches(parsed, VEvent(TimeRange("20260908T000000Z", "20260909T000000Z"))));
    }

    [Fact]
    public void AFloatingEvent_IsJudgedInTheCalendarsZone_SoTwoCalendarsDisagree()
    {
        // 09:00 with no zone at all: 07:00Z in Brussels, 21:00Z the day before in Auckland.
        var parsed = Load(Ics.Single("DTSTART:20260907T090000", "DTEND:20260907T100000"));
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange("20260907T060000Z", "20260907T080000Z")));

        Assert.True(CalendarQueryFilter.Matches(parsed, spec, Ics.Zone));
        Assert.False(CalendarQueryFilter.Matches(parsed, spec, Auckland));
    }

    [Fact]
    public void AnExdateRemovingTheOnlyInstanceOfTheWindow_LeavesNoMatch() =>
        Assert.False(Matches(Load(Ics.WeeklyWithExdateAndOverride()),
            VEvent(TimeRange("20260921T000000Z", "20260922T000000Z"))));

    [Fact]
    public void AnOverrideMovedIntoTheWindow_Matches_AndTheSlotItLeft_DoesNot()
    {
        var parsed = Load(Ics.WeeklyWithExdateAndOverride());   // the 14th moved 07:00Z → 09:00Z

        Assert.True(Matches(parsed, VEvent(TimeRange("20260914T083000Z", "20260914T100000Z"))));
        Assert.False(Matches(parsed, VEvent(TimeRange("20260914T070000Z", "20260914T080000Z"))));
    }

    // ---- the VALARM time-range ---------------------------------------------------------------

    [Fact]
    public void ARelativeTrigger_FiresBeforeTheInstance()
    {
        var parsed = Load(Ics.FromPhone());   // TRIGGER:-PT15M on a 07:00Z instance

        Assert.True(Matches(parsed, Alarm("20260907T064000Z", "20260907T065000Z")));
        Assert.False(Matches(parsed, Alarm("20260907T060000Z", "20260907T063000Z")));
    }

    [Fact]
    public void ATriggerRelatedToTheEnd_IsMeasuredFromTheEnd() =>
        // TRIGGER;RELATED=END:-PT5M on an instance ending 08:00Z: 07:55Z.
        Assert.True(Matches(Load(Ics.FromPhone()), Alarm("20260907T075000Z", "20260907T080000Z")));

    [Fact]
    public void AnAbsoluteTrigger_FiresAtTheInstantItPins()
    {
        var parsed = Load(Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z",
            extra: "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER;VALUE=DATE-TIME:20260906T120000Z\r\n"
                   + "DESCRIPTION:x\r\nEND:VALARM"));

        Assert.True(Matches(parsed, Alarm("20260906T110000Z", "20260906T130000Z")));
        Assert.False(Matches(parsed, Alarm("20260906T130000Z", "20260906T140000Z")));
    }

    [Fact]
    public void ATriggerAWeekBeforeItsInstance_IsPastTheDayOfSlack_TheAssumedBound()
    {
        // -P1W on the 7th 09:00Z fires on August 31st: the instance sits outside the walked
        // [from − 1 day, to + 1 day[, so the query answers incomplete rather than reread everything.
        var parsed = Load(Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T100000Z",
            extra: "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-P1W\r\nDESCRIPTION:x\r\nEND:VALARM"));

        Assert.False(Matches(parsed, Alarm("20260831T080000Z", "20260831T100000Z")));
        Assert.True(Matches(parsed, VEvent(TimeRange("20260907T080000Z", "20260907T100000Z"))));
    }

    [Fact]
    public void AnAlarmOnAnAllDayInstance_RingsAtTheCalendarsMidnight()
    {
        // -PT15M before the 7th: 23:45 Brussels on the 6th is 21:45Z.
        var parsed = Load(Ics.Single("DTSTART;VALUE=DATE:20260907", "DTEND;VALUE=DATE:20260908",
            extra: "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:x\r\nEND:VALARM"));

        Assert.True(Matches(parsed, Alarm("20260906T214000Z", "20260906T215000Z")));
        Assert.False(Matches(parsed, Alarm("20260906T234000Z", "20260906T235000Z")));
    }

    [Fact]
    public void AnAlarmTimeRange_SeesARepetition()
    {
        // RFC 5545 3.8.6.2: REPEAT:5, DURATION:PT10M rings again at 23:10 ... 23:50; the window
        // below excludes the 23:00 trigger itself and only the fifth ring falls inside it.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:5", "DURATION:PT10M"));

        Assert.True(Matches(parsed, Alarm("20270101T234500Z", "20270102T000000Z")));
    }

    [Fact]
    public void AVAlarmFilter_NamesAnAlarmOrItsAbsence()
    {
        var alarmed = Load(Ics.Single("DTSTART:20260907T090000Z", null,
            extra: "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nDESCRIPTION:x\r\nEND:VALARM"));

        Assert.True(Matches(alarmed, VEvent(Comp("VALARM"))));
        Assert.False(Matches(Load(Ics.Rule("FREQ=WEEKLY")), VEvent(Comp("VALARM"))));
        Assert.True(Matches(Load(Ics.Rule("FREQ=WEEKLY")), VEvent(Comp("VALARM", IsNotDefined()))));
        Assert.False(Matches(alarmed, VEvent(Comp("VALARM", IsNotDefined()))));
    }

    // ---- prop-filter, text-match and the collations ------------------------------------------

    [Fact]
    public void Octet_IsCaseSensitive_AsciiCasemap_IsNot_AndAsciiIsTheDefault()
    {
        var parsed = Load(Ics.FromPhone());   // SUMMARY:Standup

        Assert.False(Matches(parsed, VEvent(Prop("SUMMARY", Text("standup", collation: DavCollation.Octet)))));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("standup", collation: DavCollation.AsciiCasemap)))));
        // The calendar's default, not the book's.
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("standup")))));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("standup", collation: "default")))));
    }

    [Fact]
    public void AsciiCasemap_LeavesAccentsAlone()
    {
        var parsed = Load(Ics.Single("DTSTART:20260907T090000Z", null, extra: "SUMMARY:Réunion"));

        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("RéUNION")))));
        Assert.False(Matches(parsed, VEvent(Prop("SUMMARY", Text("RÉUNION")))));
    }

    [Fact]
    public void NegateCondition_InvertsSomeInstanceMatches()
    {
        var parsed = Load(Ics.FromPhone());

        Assert.False(Matches(parsed, VEvent(Prop("SUMMARY", Text("Standup", negate: true)))));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("Retro", negate: true)))));
    }

    [Fact]
    public void AMatchType_IsHonoured_WhenAClientWritesTheCardDavAttribute()
    {
        var parsed = Load(Ics.FromPhone());

        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("Standup", matchType: "equals")))));
        Assert.False(Matches(parsed, VEvent(Prop("SUMMARY", Text("Stand", matchType: "equals")))));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("Stand", matchType: "starts-with")))));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("dup", matchType: "ends-with")))));
    }

    [Fact]
    public void AParamFilter_IsSatisfiedByTheOneComponentThatCarriesIt()
    {
        // The master has no ATTENDEE at all; the override has one with PARTSTAT=ACCEPTED.
        var parsed = Load(Ics.WithAttendees());

        Assert.True(Matches(parsed, VEvent(Prop("ATTENDEE", Param("PARTSTAT", Text("ACCEPTED"))))));
        Assert.False(Matches(parsed, VEvent(Prop("ATTENDEE", Param("PARTSTAT", Text("DECLINED"))))));
        Assert.True(Matches(parsed, VEvent(Prop("ATTENDEE", Param("PARTSTAT")))));
        Assert.False(Matches(parsed, VEvent(Prop("ATTENDEE", Param("PARTSTAT", IsNotDefined())))));
        Assert.True(Matches(parsed, VEvent(Prop("ATTENDEE", Param("DELEGATED-TO", IsNotDefined())))));
    }

    [Fact]
    public void ANegatedParamFilter_DoesNotMatchAPropertyThatCarriesNoSuchParameter()
    {
        // RFC 4791 § 9.7.3: the param-filter's conditions apply to the parameters the property
        // carries. « TZID does not contain Paci » is not true of a DTSTART that has no TZID at all
        // — MatchesPropFilter has held this guard two lines above since 5c; this one lacked it.
        var allDay = Load(Ics.Single("DTSTART;VALUE=DATE:20260907", "DTEND;VALUE=DATE:20260908"));
        var filter = VEvent(Prop("DTSTART", Param("TZID", Text("Paci", negate: true))));

        Assert.False(Matches(allDay, filter));
    }

    [Fact]
    public void ANegatedParamFilter_StillMatchesAParameterWhoseValueDiffers()
    {
        var zoned = Load(Ics.Single("DTSTART;TZID=" + Ics.Zone + ":20260907T090000",
            "DTEND;TZID=" + Ics.Zone + ":20260907T100000"));

        Assert.True(Matches(zoned, VEvent(Prop("DTSTART", Param("TZID", Text("Paci", negate: true))))));
    }

    [Fact]
    public void AParticipant_MatchesOnItsAddress() =>
        Assert.True(Matches(Load(Ics.WithAttendees()), VEvent(Prop("ATTENDEE", Text("lea@example.org")))));

    [Fact]
    public void IsNotDefined_OnAProperty()
    {
        Assert.True(Matches(Load(Ics.Rule("FREQ=WEEKLY")), VEvent(Prop("LOCATION", IsNotDefined()))));
        Assert.False(Matches(Load(Ics.Single("DTSTART:20260907T090000Z", null, extra: "LOCATION:Room 4")),
            VEvent(Prop("LOCATION", IsNotDefined()))));
        // The master names a LOCATION, the override does not: one component satisfies it.
        Assert.True(Matches(Load(Ics.FromPhone()), VEvent(Prop("LOCATION", IsNotDefined()))));
    }

    [Fact]
    public void EveryClause_MustHoldOnTheSameComponent()
    {
        // FromPhone: the master carries LOCATION and the alarms, the moved override neither.
        var parsed = Load(Ics.FromPhone());
        var ringing = Comp("VALARM", TimeRange("20260907T064000Z", "20260907T065000Z"));

        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("Standup")), Prop("LOCATION", Text("Room")))));
        Assert.False(Matches(parsed, VEvent(Prop("SUMMARY", Text("(moved)")), Prop("LOCATION", Text("Room")))));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("Standup")), ringing)));
        Assert.False(Matches(parsed, VEvent(Prop("SUMMARY", Text("(moved)")), ringing)));
        Assert.True(Matches(parsed, VEvent(Prop("SUMMARY", Text("(moved)")), Comp("VALARM", IsNotDefined()))));
    }

    [Fact]
    public void ATimeRangeAndAPropFilter_AreSatisfiedByOneComponent_NeverByTwo()
    {
        // RFC 4791 § 9.7.1: « the targeted calendar component » — one component of the resource
        // answers every clause together. reports.xml/time-range t7: the master had the hour and
        // the override had the summary, and the file came back.
        var parsed = Load(Ics.WeeklyWithExdateAndOverride());   // 7th 07:00Z master, 14th 09:00Z override
        var filter = VEvent(TimeRange("20260907T070000Z", "20260907T080000Z"), Prop("SUMMARY", Text("moved")));

        Assert.False(Matches(parsed, filter));
    }

    [Fact]
    public void ATimeRangeAndAPropFilter_MatchWhenOneComponentCarriesBoth()
    {
        var parsed = Load(Ics.Single("DTSTART:20270103T190000Z", "DTEND:20270103T200000Z", extra: "SUMMARY:changed"));

        Assert.True(Matches(parsed, VEvent(TimeRange("20270103T190000Z", "20270103T200000Z"),
            Prop("SUMMARY", Text("changed")))));
    }

    [Fact]
    public void ATimeRangeAlone_StillReadsTheWholeResource()
    {
        // No prop-filter, no alarm filter: the window is asked of the file, and an override that
        // falls inside it answers for the file. Nothing about this changes.
        var parsed = Load(Ics.WeeklyWithExdateAndOverride());   // the 14th moved 07:00Z -> 09:00Z

        Assert.True(Matches(parsed, VEvent(TimeRange("20260914T083000Z", "20260914T100000Z"))));
    }

    [Fact]
    public void ATimeRangeAndAPropFilter_TheOverridesLaterInstance_StillCountsDespiteAnEarlierMasterHit()
    {
        // The window holds two instances: the master's (day one) and the override's (day two,
        // overridden but not moved in time). Narrowing Overlaps to the override component must not
        // stop at the master's earlier hit and answer false — the walk has to keep going.
        var parsed = Load(Ics.RuleWithOverrideInUtc("FREQ=DAILY", "20260908T090000Z"));

        Assert.True(Matches(parsed, VEvent(TimeRange("20260907T090000Z", "20260909T000000Z"),
            Prop("SUMMARY", Text("Moved")))));
    }

    [Fact]
    public void APropertyAbsentFromTheFile_FailsWithoutAnError() =>
        Assert.False(Matches(Load(Ics.Rule("FREQ=WEEKLY")), VEvent(Prop("LOCATION", Text("Room")))));

    [Fact]
    public void ADateTime_ComparesAsTheTextTheFileSpells()
    {
        Assert.True(Matches(Load(Ics.Rule("FREQ=WEEKLY")), VEvent(Prop("DTSTART", Text("20260907T090000")))));
        Assert.True(Matches(Load(Ics.RuleInUtc("FREQ=WEEKLY")),
            VEvent(Prop("DTSTART", Text("20260907T090000Z", matchType: "equals")))));
        Assert.True(Matches(Load(Ics.AllDayWeekly()), VEvent(Prop("DTSTART", Text("20260907", matchType: "equals")))));
    }

    [Fact]
    public void EveryClauseIsConjunctive()
    {
        var parsed = Load(Ics.FromPhone());
        var window = TimeRange("20260907T000000Z", "20260908T000000Z");

        Assert.True(Matches(parsed, VEvent(window, Prop("SUMMARY", Text("Standup")), Comp("VALARM"))));
        Assert.False(Matches(parsed, VEvent(window, Prop("SUMMARY", Text("Retro")), Comp("VALARM"))));
        Assert.False(Matches(parsed, VEvent(TimeRange("20260908T000000Z", "20260909T000000Z"),
            Prop("SUMMARY", Text("Standup")))));
    }

    // ---- the column preselection -------------------------------------------------------------

    [Fact]
    public void Columns_APlainEquals_OnTheThreeColumns()
    {
        var spec = CalendarQueryFilter.Parse(VEvent(
            Prop("STATUS", Text("confirmed", matchType: "equals")),
            Prop("TRANSP", Text("TRANSPARENT", matchType: "equals")),
            Prop("CLASS", Text(" private ", matchType: "equals"))));

        // Spelled as the projection spells the column: trimmed and upper-cased.
        Assert.Equal(new EventColumnFilter("CONFIRMED", "TRANSPARENT", "PRIVATE"), CalendarQueryFilter.Columns(spec));
    }

    [Fact]
    public void Columns_NothingElseReachesTheStore()
    {
        Assert.Equal(EventColumnFilter.None, Columns(Prop("STATUS", Text("CONFIRMED"))));
        Assert.Equal(EventColumnFilter.None, Columns(Prop("STATUS", Text("CONFIRMED", matchType: "equals", negate: true))));
        Assert.Equal(EventColumnFilter.None, Columns(Prop("STATUS", IsNotDefined())));
        Assert.Equal(EventColumnFilter.None,
            Columns(Prop("STATUS", Text("CONFIRMED", matchType: "equals"), Param("X-REASON"))));
        Assert.Equal(EventColumnFilter.None, Columns(Prop("SUMMARY", Text("CONFIRMED", matchType: "equals"))));
        // A value the column could not hold whole: the file alone can answer it.
        Assert.Equal(EventColumnFilter.None,
            Columns(Prop("STATUS", Text(new string('X', IcsProjector.MaxCodeLength + 1), matchType: "equals"))));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static void AssertRefused(XName condition, XElement filter)
    {
        var thrown = Assert.Throws<DavPreconditionException>(() => CalendarQueryFilter.Parse(filter));
        Assert.Equal(condition, thrown.Condition);
    }

    private static bool Matches(IcsCalendar parsed, XElement filter) =>
        CalendarQueryFilter.Matches(parsed, CalendarQueryFilter.Parse(filter), Ics.Zone);

    private static EventColumnFilter Columns(XElement propFilter) =>
        CalendarQueryFilter.Columns(CalendarQueryFilter.Parse(VEvent(propFilter)));

    private static IcsCalendar Load(string ics) => IcsDocument.TryLoad(ics)!;

    private static XElement Filter(params object[] children) => new(DavXml.CalDav + "filter", children);

    [Fact]
    public void AnAlarmWindowAlone_StillNarrowsThePreselection()
    {
        var spec = CalendarQueryFilter.Parse(
            Filter(Comp("VCALENDAR", Comp("VEVENT",
                Comp("VALARM", TimeRange("20260201T000000Z", "20260301T000000Z")),
                Comp("VALARM", TimeRange("20260115T000000Z", "20260210T000000Z"))))));

        // iOS sends exactly this shape. Left unnarrowed, every row of the calendar is read, parsed
        // and re-expanded once per alarmed component, inside the snapshot transaction — minutes of
        // CPU for a few hundred bytes. An alarm only fires from an instance, so the union of the
        // alarm windows bounds the rows worth looking at; CandidatesAsync widens by the same day
        // AlarmFires does.
        Assert.Null(spec.TimeRange);
        Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), spec.Preselection!.FromUtc);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), spec.Preselection.ToUtc);
    }

    [Fact]
    public void ThePreselection_IsTheEventsOwnWindowWhenItHasOne_AndNothingWithoutEither()
    {
        var scoped = CalendarQueryFilter.Parse(
            Filter(Comp("VCALENDAR", Comp("VEVENT",
                TimeRange("20260601T000000Z", "20260701T000000Z"),
                Comp("VALARM", TimeRange("20260101T000000Z", "20261231T000000Z"))))));

        Assert.Equal(scoped.TimeRange, scoped.Preselection);
        Assert.Null(CalendarQueryFilter.Parse(VEvent()).Preselection);
    }

    private static XElement VEvent(params object[] children) => Filter(Comp("VCALENDAR", Comp("VEVENT", children)));

    private static XElement Alarm(string start, string end) => VEvent(Comp("VALARM", TimeRange(start, end)));

    private static XElement Comp(string name, params object[] children) =>
        new(DavXml.CalDav + "comp-filter", new XAttribute("name", name), children);

    private static XElement Prop(string name, params object[] children) =>
        new(DavXml.CalDav + "prop-filter", new XAttribute("name", name), children);

    private static XElement Param(string name, params object[] children) =>
        new(DavXml.CalDav + "param-filter", new XAttribute("name", name), children);

    private static XElement IsNotDefined() => new(DavXml.CalDav + "is-not-defined");

    private static XElement Text(string value, string? collation = null, string? matchType = null, bool negate = false)
    {
        var element = new XElement(DavXml.CalDav + "text-match", value);
        if (collation is not null) element.SetAttributeValue("collation", collation);
        if (matchType is not null) element.SetAttributeValue("match-type", matchType);
        if (negate) element.SetAttributeValue("negate-condition", "yes");
        return element;
    }

    private static XElement TimeRange(string? start, string? end)
    {
        var element = new XElement(DavXml.CalDav + "time-range");
        if (start is not null) element.SetAttributeValue("start", start);
        if (end is not null) element.SetAttributeValue("end", end);
        return element;
    }
}
