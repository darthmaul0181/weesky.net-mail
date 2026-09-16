using Moq;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Calendar;
using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationReaderTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly Guid Work = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private readonly User _user = new("alice@weesky.be") { WebmailUid = WebmailUid };
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<ICalendarEventStore> _events = new();

    private static string Fixture(string name) => InvitationParserTests.Fixture(name);
    private static MailCalendarPart Part(string name) => new("2", Fixture(name), false);

    private InvitationReader Create(params string[] addresses)
    {
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(addresses.Length == 0 ? ["alice@weesky.be", "alice@weesky.net"] : addresses);
        _events.Setup(e => e.FindByUidAsync(WebmailUid, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new InvitationReader(_addresses.Object, _events.Object);
    }

    /// <summary>A stored file carries no METHOD — the guards refuse one — so the parser would call it
    /// no invitation at all: its UID is read off the component, the way the reader itself reads it.</summary>
    private static string UidOf(string ics) =>
        IcsDocument.Components(IcsDocument.TryLoad(ics)!).First().Uid!;

    private void Stored(string ics, Guid calendar, string davName = "abc.ics", params (string Ics, Guid Calendar)[] more)
    {
        var uid = UidOf(ics);
        List<StoredEventRef> rows = [new(Guid.NewGuid(), calendar, davName, ics, null, IcsDocument.HashOf(ics))];
        rows.AddRange(more.Select(m => new StoredEventRef(Guid.NewGuid(), m.Calendar, "other.ics", m.Ics, null, IcsDocument.HashOf(m.Ics))));
        _events.Setup(e => e.FindByUidAsync(WebmailUid, uid, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
    }

    [Fact]
    public async Task AFreshRequest_IsAbsent_AddressedToThePrimary_NeedsAction()
    {
        var block = (await Create().ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Equal(InvitationMethod.Request, block.Method);
        Assert.Equal(InvitationPresence.Absent, block.InCalendar);
        Assert.Equal("alice@weesky.be", block.AddressedTo);
        Assert.Equal("NEEDS-ACTION", block.FilePartStat);
        Assert.Null(block.SavedPartStat);
        Assert.Null(block.CalendarId);
        Assert.Equal("2", block.Part);
        // Alice@Weesky.be reads back domain-lowered: IcsProjector.Address goes through System.Uri.
        Assert.Equal(new[] { "marc.dupont@example.org", "Alice@weesky.be", "jean@example.net" },
                     block.Attendees.Select(a => a.Email));
        Assert.False(block.Unreadable);
    }

    [Fact]
    public async Task AddressedTo_MatchesAnAlias_AndTheUsersListOrderWins()
    {
        var block = (await Create().ReadAsync(_user, Conn, Part("thunderbird-request"), CancellationToken.None))!;
        Assert.Equal("alice@weesky.net", block.AddressedTo);
    }

    [Fact]
    public async Task AddressedTo_OnAConnectedAccount_IsItsAddress()
    {
        var gmail = TestConnections.Connected(Guid.NewGuid().ToString(), "alice.dupont@gmail.com", "pw");
        _addresses.Setup(a => a.ForAccountAsync(_user, gmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["alice.dupont@gmail.com"]);
        var reader = Create();
        var ics = Fixture("google-request").Replace("mailto:jean@example.net", "mailto:Alice.Dupont@gmail.com");

        var block = (await reader.ReadAsync(_user, gmail, new MailCalendarPart("2", ics, false), CancellationToken.None))!;

        Assert.Equal("alice.dupont@gmail.com", block.AddressedTo);
    }

    [Fact]
    public async Task Forwarded_HasNoAddressedTo_AndNoFilePartStat()
    {
        var block = (await Create("someone@weesky.be").ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Null(block.AddressedTo);
        Assert.Null(block.FilePartStat);
    }

    [Fact]
    public async Task Current_WhenTheStoredSequenceIsTheSame_CarriesTheSavedAnswer()
    {
        var reader = Create();
        var stored = PartStatRewriter.Rewrite(Fixture("google-request"), "alice@weesky.be", "ACCEPTED")!;
        Stored(stored, Personal);

        var block = (await reader.ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Current, block.InCalendar);
        Assert.Equal("ACCEPTED", block.SavedPartStat);
        Assert.Equal("NEEDS-ACTION", block.FilePartStat);
        Assert.Equal(Personal, block.CalendarId);
    }

    [Fact]
    public async Task Outdated_WhenTheReceivedSequenceIsHigher()
    {
        var reader = Create();
        Stored(Fixture("google-request"), Personal);
        var newer = Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:1");

        var block = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("2", newer, false), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Outdated, block.InCalendar);
    }

    [Fact]
    public async Task Newer_WhenTheReceivedSequenceIsLower_ForARequestAndForACancel()
    {
        var reader = Create();
        Stored(Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:5"), Personal);

        var request = (await reader.ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;
        var cancel = (await reader.ReadAsync(_user, Conn, Part("google-cancel"), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Newer, request.InCalendar);
        Assert.Equal(InvitationPresence.Newer, cancel.InCalendar);
    }

    [Fact]
    public async Task Cancelled_WhenACancelFindsTheEvent_AbsentOtherwise()
    {
        var reader = Create();
        var absent = (await reader.ReadAsync(_user, Conn, Part("google-cancel"), CancellationToken.None))!;
        Stored(Fixture("google-request"), Personal);
        var present = (await reader.ReadAsync(_user, Conn, Part("google-cancel"), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Absent, absent.InCalendar);
        Assert.Equal(InvitationPresence.Cancelled, present.InCalendar);
    }

    [Fact]
    public async Task TheSameUidInTwoCalendars_TheFirstRowWins()
    {
        var reader = Create();
        Stored(Fixture("google-request"), Personal, "p.ics", (Fixture("google-request"), Work));

        var block = (await reader.ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Equal(Personal, block.CalendarId);
    }

    [Fact]
    public async Task OccurrenceOnly_NeverSearchesTheCalendar()
    {
        var block = (await Create().ReadAsync(_user, Conn, Part("occurrence-cancel"), CancellationToken.None))!;

        Assert.True(block.OccurrenceOnly);
        Assert.Equal(InvitationPresence.Absent, block.InCalendar);
        _events.Verify(e => e.FindByUidAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static StoredEventRef Invited(string? owner = "webmail", string? ics = null)
    {
        var file = ics ?? Fixture("webmail-invited");
        return new(Guid.NewGuid(), Guid.NewGuid(), "web.ics", file, owner, IcsDocument.HashOf(file));
    }

    private static MailCalendarPart ReplyPart(string fixture) =>
        new("2", Fixture(fixture).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222"), false);

    [Fact]
    public async Task Reply_ToAnEventTheWebmailInvited_IsApplicable_AndSaysWhatTheFileHolds()
    {
        var reader = Create();
        var stored = Invited();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([stored]);

        var block = (await reader.ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!;

        Assert.Equal(InvitationMethod.Reply, block.Method);
        Assert.Equal(new InvitationReply("marc.dupont@example.org", "Marc Dupont", "DECLINED", ReplyStatus.Applicable, Applied: false), block.Reply);
        Assert.Equal(InvitationPresence.Current, block.InCalendar);
        Assert.Equal(stored.CalendarId, block.CalendarId);
        Assert.Equal("NEEDS-ACTION", block.SavedPartStat);
        Assert.Null(block.AddressedTo);
        Assert.Null(block.FilePartStat);
        _addresses.Verify(a => a.ForAccountAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reply_AlreadyInTheFile_IsApplied()
    {
        var reader = Create();
        var stored = Invited(ics: Fixture("webmail-invited").Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=DECLINED"));
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([stored]);

        var block = (await reader.ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!;

        Assert.True(block.Reply!.Applied);
        Assert.Equal(ReplyStatus.Applicable, block.Reply.Status);
    }

    [Fact]
    public async Task Reply_ToASeries_IsAppliedOnlyOnceEveryComponentHoldsTheAnswer()
    {
        var reader = Create();
        var series = Fixture("webmail-invited-override");
        var masterOnly = new System.Text.RegularExpressions.Regex("PARTSTAT=NEEDS-ACTION").Replace(series, "PARTSTAT=DECLINED", 1);
        var reply = new MailCalendarPart("2", ReplyPart("google-reply").Ics.Replace("SEQUENCE:0", "SEQUENCE:1"), false);

        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([Invited(ics: masterOnly)]);
        var half = (await reader.ReadAsync(_user, Conn, reply, CancellationToken.None))!;
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Invited(ics: series.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=DECLINED"))]);
        var whole = (await reader.ReadAsync(_user, Conn, reply, CancellationToken.None))!;

        Assert.Equal("DECLINED", half.SavedPartStat);
        Assert.Equal((ReplyStatus.Applicable, false), (half.Reply!.Status, half.Reply.Applied));
        Assert.Equal((ReplyStatus.Applicable, true), (whole.Reply!.Status, whole.Reply.Applied));
    }

    [Theory]
    [InlineData(null, ReplyStatus.UnknownUid, InvitationPresence.Absent)]
    [InlineData("", ReplyStatus.NotOwner, InvitationPresence.Current)]
    public async Task Reply_ToAnUnknownUid_OrAnEventNotInvitedHere_IsNotApplicable(string? owner, ReplyStatus status, InvitationPresence presence)
    {
        var reader = Create();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner is null ? [] : [Invited(owner: null)]);

        var block = (await reader.ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!;

        Assert.Equal(status, block.Reply!.Status);
        Assert.Equal(presence, block.InCalendar);
    }

    [Fact]
    public async Task Reply_FromSomeoneNotInvited_OrForOneDate_OrStale_IsNotApplicable()
    {
        var reader = Create();
        var stored = Invited(ics: Fixture("webmail-invited").Replace("SEQUENCE:0", "SEQUENCE:2"));
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([stored]);
        Assert.Equal(ReplyStatus.Stale, (await reader.ReadAsync(_user, Conn, ReplyPart("google-reply"), CancellationToken.None))!.Reply!.Status);

        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([Invited()]);
        var stranger = new MailCalendarPart("2", ReplyPart("google-reply").Ics.Replace("marc.dupont@example.org", "paul@example.org"), false);
        Assert.Equal(ReplyStatus.UnknownAttendee, (await reader.ReadAsync(_user, Conn, stranger, CancellationToken.None))!.Reply!.Status);

        var oneDate = (await reader.ReadAsync(_user, Conn, ReplyPart("google-reply-occurrence"), CancellationToken.None))!;
        Assert.Equal(ReplyStatus.OccurrenceOnly, oneDate.Reply!.Status);
        Assert.True(oneDate.OccurrenceOnly);
        _events.Verify(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Reply_MatchesTheGuest_WhateverTheCase()
    {
        var reader = Create();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync([Invited()]);
        var block = (await reader.ReadAsync(_user, Conn, ReplyPart("outlook-reply-upper"), CancellationToken.None))!;
        Assert.Equal(ReplyStatus.Applicable, block.Reply!.Status);
        Assert.Equal("TENTATIVE", block.Reply.PartStat);
    }

    private void Holding(params StoredEventRef[] rows) =>
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", It.IsAny<CancellationToken>())).ReturnsAsync(rows);

    private static MailCalendarPart Answer(string partStat = "DECLINED", string? dtStamp = "20260913T100000Z")
    {
        var ics = ReplyPart("google-reply").Ics.Replace("PARTSTAT=DECLINED", "PARTSTAT=" + partStat);
        return new("2", dtStamp is null ? ics.Replace("DTSTAMP:20260913T100000Z\r\n", "") : ics.Replace("DTSTAMP:20260913T100000Z", "DTSTAMP:" + dtStamp), false);
    }

    private static string AnsweredFile(string partStat, string stamp) =>
        PartStatRewriter.Rewrite(Fixture("webmail-invited"), "marc.dupont@example.org", partStat, stamp)!;

    [Fact]
    public async Task Reply_PicksTheGuestTheFileNames_EvenWhenOnlyAnOverrideNamesThem()
    {
        var reader = Create();
        var marcOnTheMaster = new System.Text.RegularExpressions.Regex("ATTENDEE;CN=Marc Dupont.*\r\n");
        Holding(Invited(ics: marcOnTheMaster.Replace(Fixture("webmail-invited-override"), "", 1)));
        var delegation = ReplyPart("google-reply").Ics.Replace("SEQUENCE:0", "SEQUENCE:1")
            .Replace("ATTENDEE;CUTYPE", "ATTENDEE;PARTSTAT=ACCEPTED:mailto:paul@example.org\r\nATTENDEE;CUTYPE");

        var block = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("2", delegation, false), CancellationToken.None))!;

        Assert.Equal(("marc.dupont@example.org", "DECLINED", ReplyStatus.Applicable, false),
            (block.Reply!.Email, block.Reply.PartStat, block.Reply.Status, block.Reply.Applied));
        Assert.Equal("NEEDS-ACTION", block.SavedPartStat);
    }

    [Theory]
    [InlineData("NEEDS-ACTION")]
    [InlineData("DELEGATED")]
    [InlineData("X-WEESKY-MAYBE")]
    public async Task Reply_SayingAnythingButYesMaybeOrNo_IsUnsupported(string partStat)
    {
        var reader = Create();
        Holding(Invited());

        var block = (await reader.ReadAsync(_user, Conn, Answer(partStat), CancellationToken.None))!;

        Assert.Equal((partStat, ReplyStatus.UnsupportedAnswer, false), (block.Reply!.PartStat, block.Reply.Status, block.Reply.Applied));
    }

    [Fact]
    public async Task Reply_WhenTheUidIsInTwoCalendars_TheEventTheWebmailInvitedAnswers()
    {
        var reader = Create();
        var invited = Invited();
        Holding(Invited(owner: null), invited);

        var block = (await reader.ReadAsync(_user, Conn, Answer(), CancellationToken.None))!;

        Assert.Equal((ReplyStatus.Applicable, invited.CalendarId), (block.Reply!.Status, block.CalendarId));
    }

    [Fact]
    public async Task Reply_OlderThanTheAnswerTheFileHolds_AtTheSameSequence_IsSuperseded()
    {
        var reader = Create();
        Holding(Invited(ics: AnsweredFile("DECLINED", "20260914T090000Z")));

        var older = (await reader.ReadAsync(_user, Conn, Answer("ACCEPTED", "20260913T100000Z"), CancellationToken.None))!;
        var same = (await reader.ReadAsync(_user, Conn, Answer("ACCEPTED", "20260914T090000Z"), CancellationToken.None))!;
        var unstamped = (await reader.ReadAsync(_user, Conn, Answer("ACCEPTED", dtStamp: null), CancellationToken.None))!;
        Holding(Invited(ics: AnsweredFile("DECLINED", "20260914T090000Z").Replace("SEQUENCE:0", "SEQUENCE:1")));
        var atSequenceOne = (await reader.ReadAsync(_user, Conn,
            new MailCalendarPart("2", Answer("ACCEPTED").Ics.Replace("SEQUENCE:0", "SEQUENCE:1"), false), CancellationToken.None))!;

        Assert.Equal(ReplyStatus.Superseded, older.Reply!.Status);
        Assert.Equal("DECLINED", older.SavedPartStat);
        Assert.Equal(ReplyStatus.Applicable, same.Reply!.Status);
        Assert.Equal(ReplyStatus.Applicable, unstamped.Reply!.Status);
        Assert.Equal(ReplyStatus.Superseded, atSequenceOne.Reply!.Status);
    }

    [Fact]
    public async Task Reply_OfTheSameAnswer_IsApplied_UnlessTheFileStampedItEarlier()
    {
        var reader = Create();
        Holding(Invited(ics: AnsweredFile("DECLINED", "20260912T100000Z")));
        var earlier = (await reader.ReadAsync(_user, Conn, Answer(), CancellationToken.None))!;
        Holding(Invited(ics: AnsweredFile("DECLINED", "20260913T100000Z")));
        var same = (await reader.ReadAsync(_user, Conn, Answer(), CancellationToken.None))!;
        var unstamped = (await reader.ReadAsync(_user, Conn, Answer(dtStamp: null), CancellationToken.None))!;

        Assert.Equal((ReplyStatus.Applicable, false), (earlier.Reply!.Status, earlier.Reply.Applied));
        Assert.Equal((ReplyStatus.Applicable, true), (same.Reply!.Status, same.Reply.Applied));
        Assert.Equal((ReplyStatus.Applicable, true), (unstamped.Reply!.Status, unstamped.Reply.Applied));
    }

    [Fact]
    public async Task Reply_WithoutAttendee_IsUnreadable()
    {
        var ics = Fixture("google-reply").Replace(
            "ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=DECLINED;CN=Marc Dupont:mailto:marc.dupont@example.org\r\n", "");

        var block = (await Create().ReadAsync(_user, Conn, new MailCalendarPart("2", ics, false), CancellationToken.None))!;

        Assert.Equal((true, InvitationReader.ReplyWithoutAttendee, "2"), (block.Unreadable, block.Reason, block.Part));
        _events.Verify(e => e.FindByUidAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unreadable_CarriesThePartAndTheReason_NothingElse()
    {
        var reader = Create();

        var tooLarge = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("3", "", true), CancellationToken.None))!;
        var garbage = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("3", "garbage", false), CancellationToken.None))!;

        Assert.True(tooLarge.Unreadable);
        Assert.Equal("3", tooLarge.Part);
        Assert.NotNull(tooLarge.Reason);
        Assert.True(garbage.Unreadable);
        _addresses.Verify(a => a.ForAccountAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
