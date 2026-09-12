using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

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
        List<StoredEventRef> rows = [new(Guid.NewGuid(), calendar, davName, ics)];
        rows.AddRange(more.Select(m => new StoredEventRef(Guid.NewGuid(), m.Calendar, "other.ics", m.Ics)));
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

    [Fact]
    public async Task Reply_IsNull_TheFileStaysAnAttachment()
    {
        Assert.Null(await Create().ReadAsync(_user, Conn, Part("google-reply"), CancellationToken.None));
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
