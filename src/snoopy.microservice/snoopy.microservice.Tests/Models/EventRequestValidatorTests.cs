using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class EventRequestValidatorTests
{
    private static EventRequest Dated() => new()
    {
        CalendarId = Guid.NewGuid(),
        Summary = "Standup",
        IsAllDay = false,
        Start = new DateTime(2026, 9, 7, 9, 0, 0),
        End = new DateTime(2026, 9, 7, 10, 0, 0),
        TimeZone = "Europe/Brussels",
    };

    private static EventRequest AllDay() => new()
    {
        CalendarId = Guid.NewGuid(),
        Summary = "Chores",
        IsAllDay = true,
        StartDate = new DateOnly(2026, 9, 7),
        EndDateInclusive = new DateOnly(2026, 9, 9),
    };

    [Fact]
    public void Validate_DatedNominal_MapsEveryField()
    {
        var result = EventRequestValidator.Validate(Dated());

        Assert.True(result.IsSuccess);
        Assert.Equal("Standup", result.Value.Summary);
        Assert.False(result.Value.IsAllDay);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0), result.Value.Start);
        Assert.Equal(new DateTime(2026, 9, 7, 10, 0, 0), result.Value.End);
        Assert.Equal("Europe/Brussels", result.Value.TimeZone);
        Assert.Null(result.Value.StartDate);
        Assert.Null(result.Value.EndDateInclusive);
    }

    // EndDateInclusive travels unchanged from the request to the write — the value the editor
    // sent is the value that survives, never bumped to an exclusive day here, that is the
    // composer's job.
    [Fact]
    public void Validate_AllDayNominal_EndDateIsInclusive()
    {
        var result = EventRequestValidator.Validate(AllDay());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAllDay);
        Assert.Equal(new DateOnly(2026, 9, 7), result.Value.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 9), result.Value.EndDateInclusive);
        Assert.Null(result.Value.Start);
    }

    [Fact]
    public void Validate_AllDaySingleDay_EndEqualsStart_IsAccepted()
    {
        var request = AllDay();
        request.EndDateInclusive = request.StartDate;

        Assert.True(EventRequestValidator.Validate(request).IsSuccess);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Validate_Refuses(Func<EventRequest> build, string message)
    {
        var result = EventRequestValidator.Validate(build());

        Assert.True(result.IsFailure);
        Assert.Equal(message, result.Error);
    }

    public static IEnumerable<object[]> Refusals()
    {
        yield return [Case(e => e.End = e.Start), "Start must be before end"];
        yield return [Case(e => e.StartDate = null, allDay: true), "An all-day event needs startDate"];
        yield return [Case(e => e.EndDateInclusive = null, allDay: true), "An all-day event needs endDateInclusive"];
        yield return [Case(e => e.TimeZone = "Nowhere/Land"), "Unknown time zone"];
        yield return [Case(e => e.TimeZone = null), "A dated event needs a time zone"];
        yield return [Case(e => e.Start = null), "A dated event needs a start"];
        yield return [Case(e => e.End = null), "A dated event needs an end"];
        yield return [Repeat(r => r.Frequency = "MINUTELY"), "Unknown repeat frequency"];
        yield return [Repeat(r => r.Interval = 0), "Repeat interval must be at least 1"];
        yield return
        [
            Repeat(r => { r.End = RecurrenceEnd.Count; r.Count = 5; r.Until = new DateOnly(2026, 1, 1); }),
            "Repeat: count and until are exclusive",
        ];
        yield return [Repeat(r => r.End = RecurrenceEnd.Count), "Repeat: count is required"];
        yield return [Repeat(r => r.End = RecurrenceEnd.Until), "Repeat: until is required"];
        yield return
        [
            Case(e => e.ReminderMinutesBefore = [EventRequestValidator.MaxReminderMinutes + 1]),
            $"Reminder must be between 0 and {EventRequestValidator.MaxReminderMinutes} minutes",
        ];
        yield return
        [
            Case(e => e.ReminderMinutesBefore = [-1]),
            $"Reminder must be between 0 and {EventRequestValidator.MaxReminderMinutes} minutes",
        ];
        yield return [Repeat(r => r.ByMonthDay = 0), "Repeat: byMonthDay must be between -31 and 31"];
        yield return [Repeat(r => r.ByMonthDay = 32), "Repeat: byMonthDay must be between -31 and 31"];
        yield return [Repeat(r => r.ByMonthDay = -32), "Repeat: byMonthDay must be between -31 and 31"];
        yield return [Repeat(r => r.BySetPos = 0), "Repeat: bySetPos must be between -366 and 366"];
        yield return [Repeat(r => r.BySetPos = 367), "Repeat: bySetPos must be between -366 and 366"];
        yield return [Repeat(r => r.ByDay = ["XX"]), "Repeat: unknown weekday"];
        yield return [Repeat(r => r.ByDay = ["MO", "54MO"]), "Repeat: unknown weekday"];
        yield return [Repeat(r => r.ByDay = ["0MO"]), "Repeat: unknown weekday"];
        yield return [Repeat(r => { r.BySetPos = 1; r.BySetPosDay = "MON"; }), "Repeat: unknown weekday"];
    }

    /// <summary>RFC 5545 § 3.3.10's own shapes, which the editor may not send today but a request
    /// hand-written against the API may: the bound is on the value, not on the picker.</summary>
    [Theory]
    [InlineData("MO")]
    [InlineData("su")]
    [InlineData("2MO")]
    [InlineData("-1SU")]
    [InlineData("+53FR")]
    public void Validate_AcceptsEveryLegalByDayCode(string code)
    {
        var request = Dated();
        request.Repeat = new RecurrenceRequest { Frequency = "MONTHLY", Interval = 1, ByDay = [code] };

        Assert.True(EventRequestValidator.Validate(request).IsSuccess);
    }

    [Theory]
    [InlineData(-31)]
    [InlineData(31)]
    public void Validate_AcceptsTheEdgesOfByMonthDay(int day)
    {
        var request = Dated();
        request.Repeat = new RecurrenceRequest { Frequency = "MONTHLY", Interval = 1, ByMonthDay = day };

        Assert.True(EventRequestValidator.Validate(request).IsSuccess);
    }

    /// <summary>The two fields say opposite things about whether the editor showed the rule; one of
    /// them is stale, and guessing which would drop a rule the user chose without saying so.</summary>
    [Fact]
    public void Validate_KeepRepeatWithARepeat_IsRefused()
    {
        var request = Dated();
        request.KeepRepeat = true;
        request.Repeat = new RecurrenceRequest { Frequency = "WEEKLY", Interval = 1 };

        var result = EventRequestValidator.Validate(request);

        Assert.True(result.IsFailure);
        Assert.Equal(EventRequestValidator.KeepRepeatIsExclusive, result.Error);
    }

    [Fact]
    public void Validate_KeepRepeatAlone_IsAcceptedAndCarriesNoRule()
    {
        var request = Dated();
        request.KeepRepeat = true;

        var result = EventRequestValidator.Validate(request);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.KeepRepeat);
        Assert.Null(result.Value.Repeat);
    }

    [Fact]
    public void Attendees_AreLowercased_Deduplicated_AndNamed_OrRefused()
    {
        var request = Dated();
        request.Attendees = [
            new() { Email = " Marc.Dupont@Example.org ", Name = "Marc\r\nDupont" },
            new() { Email = "marc.dupont@example.org" },
            new() { Email = "julie@example.net", Name = "  " }];

        var write = EventRequestValidator.Validate(request).Value;

        Assert.Equal([new AttendeeWrite("marc.dupont@example.org", "Marc Dupont"), new AttendeeWrite("julie@example.net", null)], write.Attendees);
        Assert.Null(write.Organizer);

        request.Attendees = [new() { Email = "not an address" }];
        Assert.Equal(EventRequestValidator.InvalidAttendee, EventRequestValidator.Validate(request).Error);
        request.Attendees = [.. Enumerable.Range(0, 101).Select(i => new AttendeeRequest { Email = $"g{i}@example.org" })];
        Assert.Equal(EventRequestValidator.TooManyAttendees, EventRequestValidator.Validate(request).Error);
        request.Attendees = null;
        Assert.Null(EventRequestValidator.Validate(request).Value.Attendees);
        request.Attendees = [];
        Assert.Empty(EventRequestValidator.Validate(request).Value.Attendees!);
    }

    // A DQUOTE cannot sit inside a parameter value (RFC 5545 § 3.2), nor can a control character;
    // a bare local part is no address a guest can be mailed at.
    [Theory]
    [InlineData("Marc \"le Grand\"\tDupont", "Marc le Grand Dupont")]
    [InlineData("\"\"", null)]
    public void Attendees_NameLosesQuotesAndControls(string raw, string? expected)
    {
        var request = Dated();
        request.Attendees = [new() { Email = "marc@example.org", Name = raw }];

        Assert.Equal(expected, EventRequestValidator.Validate(request).Value.Attendees!.Single().Name);
    }

    [Theory]
    [InlineData("marc")]
    [InlineData("")]
    [InlineData("Marc <marc@example.org>")]
    // MimeKit takes both, but the first is no mailto: URI and the second reads back
    // percent-encoded, so a guest's answer would never be matched to them again.
    [InlineData("\"marc dupont\"@example.org")]
    [InlineData("josé@example.org")]
    public void Attendees_RefuseWhatIsNotABareAddress(string email)
    {
        var request = Dated();
        request.Attendees = [new() { Email = email }];

        Assert.Equal(EventRequestValidator.InvalidAttendee, EventRequestValidator.Validate(request).Error);
    }

    // A guest the stored file already lists is that file's to keep, whatever its address; only a
    // new address has to be a plain mailto one.
    [Fact]
    public void Attendees_AnAddressTheFileAlreadyHolds_PassesAsItIs_ANewOneIsStillChecked()
    {
        // As the projector reads `sip:room@example.org`, `mailto:josé@example.org` and
        // `mailto:salle%2520mercure@…`: already decoded once, so compared as they are, never again.
        string[] stored = ["sip:room@example.org", "josé@example.org", "salle%20mercure@example.org"];
        var request = Dated();
        request.Attendees = [new() { Email = "SIP:room@example.org" }, new() { Email = " josé@example.org" }, new() { Email = "salle%20mercure@example.org" }, new() { Email = "paul@example.org" }];

        Assert.Equal(["sip:room@example.org", "josé@example.org", "salle%20mercure@example.org", "paul@example.org"],
            EventRequestValidator.Validate(request, stored).Value.Attendees!.Select(a => a.Email));

        request.Attendees = [.. request.Attendees, new() { Email = "rené@example.org" }];
        Assert.Equal(EventRequestValidator.InvalidAttendee, EventRequestValidator.Validate(request, stored).Error);
        request.Attendees = [new() { Email = "sip:room@example.org" }];
        Assert.Equal(EventRequestValidator.InvalidAttendee, EventRequestValidator.Validate(request).Error);
    }

    [Fact]
    public void Attendees_RefuseAnAddressWiderThanItsColumn()
    {
        var request = Dated();
        request.Attendees = [new() { Email = new string('a', 64) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 5)) + ".org" }];

        Assert.Equal(EventRequestValidator.InvalidAttendee, EventRequestValidator.Validate(request).Error);
    }

    [Fact]
    public void Attendees_LongNameIsCut()
    {
        var request = Dated();
        request.Attendees = [new() { Email = "marc@example.org", Name = new string('a', 150) }];

        Assert.Equal(IcsComposer.MaxCommonNameLength, EventRequestValidator.Validate(request).Value.Attendees!.Single().Name!.Length);
    }

    // The cut never leaves half of a surrogate pair, which no encoder can write.
    [Fact]
    public void Attendees_LongNameIsCutBeforeAnEmojiItWouldSplit()
    {
        var request = Dated();
        request.Attendees = [new() { Email = "marc@example.org", Name = new string('a', 99) + "😀 Dupont" }];

        Assert.Equal(new string('a', 99), EventRequestValidator.Validate(request).Value.Attendees!.Single().Name);
    }

    private static Func<EventRequest> Case(Action<EventRequest> tweak, bool allDay = false) => () =>
    {
        var request = allDay ? AllDay() : Dated();
        tweak(request);
        return request;
    };

    private static Func<EventRequest> Repeat(Action<RecurrenceRequest> tweak) => () =>
    {
        var request = Dated();
        var repeat = new RecurrenceRequest { Frequency = "WEEKLY", Interval = 1 };
        tweak(repeat);
        request.Repeat = repeat;
        return request;
    };
}
