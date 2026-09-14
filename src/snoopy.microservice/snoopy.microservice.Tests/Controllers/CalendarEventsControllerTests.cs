using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CalendarEventsControllerTests
{
    private static readonly Guid Uid = Guid.NewGuid();
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FromOffset = new(From, TimeSpan.Zero);
    private const string Zone = "Europe/Brussels";

    private readonly Mock<ICalendarEventStore> _store = new();
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<IOrganizerIdentity> _organizer = new();
    private readonly Mock<IInvitationScheduler> _scheduler = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();

    public CalendarEventsControllerTests()
    {
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), It.IsAny<WriteOrigin>(),
                It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulingReport(null, 0));
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(TestConnections.Primary("john@example.com", "pw")));
    }

    private CalendarEventsController CreateController()
    {
        _addresses.Setup(a => a.ForPrimaryAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["john@example.com", "john@example.net"]);
        _addresses.Setup(a => a.ForPrincipalAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["john@example.com", "john@example.net", "john@gmail.com"]);
        _organizer.Setup(o => o.ResolveAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizerWrite("john@example.com", "John"));
        _store.Setup(s => s.OwnPartStatsAsync(Uid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        var controller = new CalendarEventsController(_store.Object, _addresses.Object, _organizer.Object, _scheduler.Object, _connections.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }

    private static EventOccurrence Occurrence(Guid? eventId = null, Guid? calendarId = null) =>
        new(eventId ?? Guid.NewGuid(), calendarId ?? Guid.NewGuid(), "uid-1", string.Empty, false, false,
            false, Zone, From, From.AddHours(1), null, null, null, null, "Standup", null, null,
            "OPAQUE", null, false, null);

    private static Result<IReadOnlyList<EventOccurrence>> Found(params EventOccurrence[] occurrences) =>
        Result.Success<IReadOnlyList<EventOccurrence>>(occurrences);

    private static EventRequest ValidRequest(Guid? calendarId = null) => new()
    {
        CalendarId = calendarId ?? Guid.NewGuid(),
        Summary = "Standup",
        IsAllDay = false,
        Start = new DateTime(2026, 9, 7, 9, 0, 0),
        End = new DateTime(2026, 9, 7, 10, 0, 0),
        TimeZone = Zone,
    };

    private static EventUpdateRequest ValidUpdateRequest(Guid? calendarId = null) => new()
    {
        CalendarId = calendarId ?? Guid.NewGuid(),
        Summary = "Standup",
        IsAllDay = false,
        Start = new DateTime(2026, 9, 7, 9, 0, 0),
        End = new DateTime(2026, 9, 7, 10, 0, 0),
        TimeZone = Zone,
        Scope = EditScope.All,
        IfHash = "abc123",
    };

    private static EventDetail Detail(Guid? id = null, Guid? calendarId = null) =>
        new(id ?? Guid.NewGuid(), calendarId ?? Guid.NewGuid(), "uid-1", "hash-1", MinimalWrite(), null, [], null,
            true, []);

    private static EventWrite MinimalWrite() =>
        new(Guid.NewGuid(), "Standup", null, null, false,
            new DateTime(2026, 9, 7, 9, 0, 0), new DateTime(2026, 9, 7, 10, 0, 0), Zone,
            null, null, null, [], Availability.Busy, Visibility.Default, null);

    [Fact]
    public async Task Window_RefusesMoreThanFiveYears() =>
        Assert.IsType<BadRequestObjectResult>((await CreateController()
            .Window(FromOffset, FromOffset.AddYears(5).AddDays(1), Zone, CancellationToken.None)).Result);

    [Fact]
    public async Task Window_RefusesUnknownZone() =>
        Assert.IsType<BadRequestObjectResult>((await CreateController()
            .Window(FromOffset, FromOffset.AddDays(1), "Nowhere/Land", CancellationToken.None)).Result);

    [Fact]
    public async Task Window_RefusesFromNotBeforeTo() =>
        Assert.IsType<BadRequestObjectResult>((await CreateController()
            .Window(FromOffset, FromOffset, Zone, CancellationToken.None)).Result);

    [Fact]
    public async Task Window_PassesZoneAndBoundsToTheStore()
    {
        var to = FromOffset.AddDays(1);
        _store.Setup(s => s.WindowAsync(Uid, From, to.UtcDateTime, Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Found(Occurrence()));

        var result = await CreateController().Window(FromOffset, to, Zone, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<OccurrenceListResponse>(ok.Value).Occurrences);
        _store.Verify(s => s.WindowAsync(Uid, From, to.UtcDateTime, Zone, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Spec 5e: the grid draws « provisoire » from the user's own PARTSTAT, read against every
    // address the principal answers to, never from the organizer's STATUS.
    [Fact]
    public async Task Window_CarriesTheUsersOwnAnswer()
    {
        var mine = Occurrence();
        var other = Occurrence();
        var to = FromOffset.AddDays(1);
        _store.Setup(s => s.WindowAsync(Uid, From, to.UtcDateTime, Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Found(mine, other));
        var controller = CreateController();
        _store.Setup(s => s.OwnPartStatsAsync(Uid,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(mine.EventId) && ids.Contains(other.EventId)),
                It.Is<IReadOnlyCollection<string>>(a => a.Contains("john@example.com") && a.Contains("john@gmail.com")), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, string> { [mine.EventId] = "TENTATIVE" });

        var result = await controller.Window(FromOffset, to, Zone, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<OccurrenceListResponse>(ok.Value).Occurrences;
        Assert.Equal("TENTATIVE", list.Single(o => o.EventId == mine.EventId).MyPartStat);
        Assert.Null(list.Single(o => o.EventId == other.EventId).MyPartStat);
    }

    [Fact]
    public async Task Search_CarriesTheUsersOwnAnswer()
    {
        var mine = Occurrence();
        _store.Setup(s => s.SearchAsync(Uid, "dîner", It.IsAny<CancellationToken>())).ReturnsAsync([mine]);
        var controller = CreateController();
        _store.Setup(s => s.OwnPartStatsAsync(Uid, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(mine.EventId)),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, string> { [mine.EventId] = "ACCEPTED" });

        var result = await controller.Search("dîner", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("ACCEPTED", Assert.IsType<OccurrenceListResponse>(ok.Value).Occurrences.Single().MyPartStat);
    }

    [Fact]
    public async Task Get_CarriesTheUsersOwnAnswer()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Detail(id: id));
        var controller = CreateController();
        _store.Setup(s => s.OwnPartStatsAsync(Uid, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == id),
                It.Is<IReadOnlyCollection<string>>(a => a.Contains("john@gmail.com")), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, string> { [id] = "TENTATIVE" });

        var result = await controller.Get(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("TENTATIVE", Assert.IsType<EventResponse>(ok.Value).MyPartStat);
    }

    // The reviewer's exact reproduction: a non-UTC offset must convert to the same instant,
    // never get relabelled as if its wall-clock digits were already UTC.
    [Fact]
    public async Task Window_ConvertsANonUtcOffsetToTheSameUtcInstant()
    {
        var from = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.FromHours(2));
        var to = from.AddDays(1);
        _store.Setup(s => s.WindowAsync(Uid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Found());

        await CreateController().Window(from, to, Zone, CancellationToken.None);

        _store.Verify(s => s.WindowAsync(Uid,
            It.Is<DateTime>(d => d == new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) && d.Kind == DateTimeKind.Utc),
            It.Is<DateTime>(d => d.Kind == DateTimeKind.Utc),
            Zone, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>The budget is a refusal, not a truncation: a grid silently missing half its
    /// instances is worse than one told to ask for less.</summary>
    [Fact]
    public async Task Window_WhenTheStoreRefusesTheBudget_Returns400()
    {
        _store.Setup(s => s.WindowAsync(Uid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<IReadOnlyList<EventOccurrence>>(CalendarEventStore.WindowTooDense));

        var result = await CreateController().Window(FromOffset, FromOffset.AddDays(1), Zone, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Search_Returns200WithOccurrences()
    {
        _store.Setup(s => s.SearchAsync(Uid, "lunch", It.IsAny<CancellationToken>())).ReturnsAsync([Occurrence()]);

        var result = await CreateController().Search("lunch", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<OccurrenceListResponse>(ok.Value).Occurrences);
    }

    [Fact]
    public async Task Get_SaysWhetherTheUserMayInvite()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [new AttendeeProjection(null, "lea@example.net", "Léa", null, null, true)] });
        var received = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        Assert.False(received.CanInvite);

        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Detail(id));
        var own = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        Assert.True(own.CanInvite);
        _addresses.Verify(a => a.ForPrimaryAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _addresses.Verify(a => a.ForPrincipalAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // Décision 8 on the organizer's side: the user's Gmail account is another account, like anyone else's.
    // An invitation it sent is received here — no guest list to edit, no ORGANIZER to take over —
    // while the user's own answer is still read under every address the principal answers to.
    [Fact]
    public async Task AnEventOrganizedByAConnectedAccountsIdentity_IsReceived_NotTheUsersToInvite()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [new AttendeeProjection(null, "John@Gmail.com", "John", null, null, true)] });
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var detail = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        var update = await CreateController().Update(id, request, CancellationToken.None);

        Assert.False(detail.CanInvite);
        Assert.Equal(CalendarEventsController.NotOrganizer, Assert.IsType<ResultEnveloppe>(Assert.IsType<BadRequestObjectResult>(update).Value).Message);
        _store.Verify(s => s.UpdateAsync(Uid, id, It.IsAny<EditScope>(), It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Organizers on every component are the user's, under any of their addresses and spellings.
    [Fact]
    public async Task Get_EveryOrganizerTheUsers_MayInvite()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [
                new AttendeeProjection("20261019T100000", "John@Example.net", null, null, null, true),
                new AttendeeProjection(null, " JOHN@example.com", null, null, null, true)] });

        var result = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);

        Assert.True(result.CanInvite);
    }

    // The user runs the series but somebody else runs one occurrence: a save with guests would
    // write the user's ORGANIZER over that occurrence, so it is refused.
    [Fact]
    public async Task AnOverrideOrganizedBySomebodyElse_UnderTheUsersMaster_RefusesInvitation()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [
                new AttendeeProjection("20261019T100000", "lea@example.net", "Léa", null, null, true),
                new AttendeeProjection(null, " JOHN@example.com", null, null, null, true)] });
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var detail = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        var update = await CreateController().Update(id, request, CancellationToken.None);

        Assert.False(detail.CanInvite);
        Assert.Equal(CalendarEventsController.NotOrganizer, Assert.IsType<ResultEnveloppe>(Assert.IsType<BadRequestObjectResult>(update).Value).Message);
    }

    // An invitation to one occurrence arrives as an override without its master: its ORGANIZER is
    // still somebody else.
    [Fact]
    public async Task Get_WithoutAMaster_ReadsAnyComponentsOrganizer()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [
                new AttendeeProjection("20261019T100000", "john@example.com", null, "REQ-PARTICIPANT", "NEEDS-ACTION", false),
                new AttendeeProjection("20261019T100000", "lea@example.net", "Léa", null, null, true)] });

        var result = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);

        Assert.False(result.CanInvite);
    }

    // Without a master, every component's ORGANIZER must be the user's: one foreign organizer is enough to refuse.
    [Fact]
    public async Task WithoutAMaster_EveryOrganizerMustBeTheUsers()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail(id) with { Attendees = [
                new AttendeeProjection("20261012T100000", "john@example.com", null, null, null, true),
                new AttendeeProjection("20261019T100000", "lea@example.net", "Léa", null, null, true)] });
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var detail = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>((await CreateController().Get(id, CancellationToken.None)).Result).Value);
        var update = await CreateController().Update(id, request, CancellationToken.None);

        Assert.False(detail.CanInvite);
        Assert.Equal(CalendarEventsController.NotOrganizer, Assert.IsType<ResultEnveloppe>(Assert.IsType<BadRequestObjectResult>(update).Value).Message);
    }

    [Fact]
    public async Task Get_Returns200WithTheDetail()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Detail(id: id));

        var result = await CreateController().Get(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, Assert.IsType<EventResponse>(ok.Value).Id);
    }

    [Fact]
    public async Task Get_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.GetAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((EventDetail?)null);

        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithAttendees_WritesTheResolvedOrganizer()
    {
        var request = ValidRequest();
        request.Attendees = [new() { Email = "marc@example.org", Name = "Marc" }];
        EventWrite? written = null;
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, EventWrite, CancellationToken>((_, w, _) => written = w)
            .ReturnsAsync(Result.Success(new EventWriteResult(Guid.NewGuid(), [])));

        await CreateController().Create(request, CancellationToken.None);

        Assert.Equal(new OrganizerWrite("john@example.com", "John"), written!.Organizer);
        Assert.Equal([new AttendeeWrite("marc@example.org", "Marc")], written.Attendees);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithoutAttendees_ResolvesNoOrganizer(bool emptyList)
    {
        var request = ValidRequest();
        if (emptyList) request.Attendees = [];
        EventWrite? written = null;
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, EventWrite, CancellationToken>((_, w, _) => written = w)
            .ReturnsAsync(Result.Success(new EventWriteResult(Guid.NewGuid(), [])));

        await CreateController().Create(request, CancellationToken.None);

        Assert.Null(written!.Organizer);
        _organizer.Verify(o => o.ResolveAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithAttendees_OnAnEventSomebodyElseOrganizes_Is400()
    {
        var id = Guid.NewGuid();
        var received = Detail(id) with { Attendees = [new AttendeeProjection(null, "lea@example.net", "Léa", null, null, true)] };
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(received);
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var result = await CreateController().Update(id, request, CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(CalendarEventsController.NotOrganizer, Assert.IsType<ResultEnveloppe>(refused.Value).Message);
        _store.Verify(s => s.UpdateAsync(Uid, id, It.IsAny<EditScope>(), It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _organizer.Verify(o => o.ResolveAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithAttendees_OnAnEventTheUserOrganizes_ByAnAlias_Proceeds()
    {
        var id = Guid.NewGuid();
        var mine = Detail(id) with { Attendees = [new AttendeeProjection(null, "John@Example.net", null, null, null, true)] };
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        EventWrite? written = null;
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, EditScope, string?, EventWrite, string?, CancellationToken>((_, _, _, _, w, _, _) => written = w)
            .ReturnsAsync(Result.Success(new EventWriteResult(id, [])));
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = "marc@example.org" }];

        var result = await CreateController().Update(id, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new OrganizerWrite("john@example.com", "John"), written!.Organizer);
    }

    // The editor sends every guest back: one it could never have typed — a SIP room, an accented
    // address — must not refuse every later save of the event it came with.
    [Fact]
    public async Task Update_KeepsAStoredGuestTheValidatorWouldRefuse_BesideANewOne_AndRefusesANewInvalidAddress()
    {
        const string room = "sip:room@example.org";
        const string jose = "josé@example.org";
        var id = Guid.NewGuid();
        var mine = Detail(id) with { Attendees = [
            new AttendeeProjection(null, "john@example.com", null, null, null, true),
            new AttendeeProjection(null, room, "Salle Mercure", null, "ACCEPTED", false),
            new AttendeeProjection(null, jose, "José", null, "TENTATIVE", false)] };
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        EventWrite? written = null;
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, EditScope, string?, EventWrite, string?, CancellationToken>((_, _, _, _, w, _, _) => written = w)
            .ReturnsAsync(Result.Success(new EventWriteResult(id, [])));
        var request = ValidUpdateRequest();
        request.Attendees = [new() { Email = room, Name = "Salle Mercure" }, new() { Email = jose, Name = "José" }, new() { Email = "marc@example.org" }];

        Assert.IsType<OkObjectResult>(await CreateController().Update(id, request, CancellationToken.None));
        Assert.Equal([room, jose, "marc@example.org"], written!.Attendees!.Select(a => a.Email));

        request.Attendees = [.. request.Attendees, new() { Email = "rené@example.org" }];
        var refused = Assert.IsType<BadRequestObjectResult>(await CreateController().Update(id, request, CancellationToken.None));
        Assert.Equal(EventRequestValidator.InvalidAttendee, Assert.IsType<ResultEnveloppe>(refused.Value).Message);
        _store.Verify(s => s.UpdateAsync(Uid, id, It.IsAny<EditScope>(), It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The composer keeps a line only on the component it rewrites: an address that is the master's
    // ORGANIZER, or a guest of one override alone, is a new guest of the master and checked as one.
    [Fact]
    public async Task Update_ChecksAnAddressStoredOnlyAsOrganizerOrOnAnOverride()
    {
        var id = Guid.NewGuid();
        var mine = Detail(id) with { Attendees = [
            new AttendeeProjection(null, "reception", null, null, null, true),
            new AttendeeProjection("20261019T100000", "sip:room@example.org", null, null, "ACCEPTED", false)] };
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        var controller = CreateController();
        _addresses.Setup(a => a.ForPrimaryAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["john@example.com", "reception"]);

        foreach (var address in new[] { "reception", "sip:room@example.org" })
        {
            var request = ValidUpdateRequest();
            request.Attendees = [new() { Email = address }];
            var refused = Assert.IsType<BadRequestObjectResult>(await controller.Update(id, request, CancellationToken.None));
            Assert.Equal(EventRequestValidator.InvalidAttendee, Assert.IsType<ResultEnveloppe>(refused.Value).Message);
        }
    }

    [Fact]
    public async Task Update_WithAttendees_OnAMissingEvent_Is404()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.GetAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync((EventDetail?)null);
        var request = ValidUpdateRequest();
        request.Attendees = [];

        Assert.IsType<NotFoundObjectResult>(await CreateController().Update(id, request, CancellationToken.None));
    }

    // Null attendees is a client that never showed the field, or a drag in the grid: nothing to
    // authorise, so neither the event nor the address list is read.
    [Fact]
    public async Task Update_WithoutAttendees_ReadsNeitherTheEventNorTheAddresses()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, [])));

        Assert.IsType<OkObjectResult>(await CreateController().Update(id, ValidUpdateRequest(), CancellationToken.None));

        _store.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _addresses.Verify(a => a.ForPrimaryAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _addresses.Verify(a => a.ForPrincipalAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WhenAccepted_Returns201WithTheId()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(new EventWriteResult(id, [])));

        var result = await CreateController().Create(ValidRequest(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Equal(id, Assert.IsType<CreatedId>(obj.Value).Id);
    }

    [Fact]
    public async Task Create_WithAnInvalidBody_Returns400()
    {
        var result = await CreateController().Create(new EventRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WhenTheCalendarIsNotFound_Returns404()
    {
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<EventWriteResult>(CalendarStore.NotFound));

        var result = await CreateController().Create(ValidRequest(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_AtTheCap_Returns400()
    {
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<EventWriteResult>(CalendarEventStore.CapReached));

        var result = await CreateController().Create(ValidRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>A creation has no stored rule to leave alone, so the flag can only be a stale one
    /// carried over from an edit: refused rather than silently making a one-off event.</summary>
    [Fact]
    public async Task Create_WithKeepRepeat_Returns400()
    {
        var request = ValidRequest();
        request.KeepRepeat = true;
        request.Repeat = new RecurrenceRequest { Frequency = "WEEKLY", Interval = 1 };

        var result = await CreateController().Create(request, CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(CalendarEventsController.KeepRepeatNeedsAnEvent,
            Assert.IsAssignableFrom<ResultEnveloppe>(refused.Value).Message);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_WithKeepRepeatAndARepeat_Returns400()
    {
        var request = ValidUpdateRequest();
        request.KeepRepeat = true;
        request.Repeat = new RecurrenceRequest { Frequency = "WEEKLY", Interval = 1 };

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(), It.IsAny<string>(),
            It.IsAny<EventWrite>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The shape the editor actually sends for a rule it cannot state: the flag alone.</summary>
    [Fact]
    public async Task Update_WithKeepRepeatAndNoRepeat_IsAccepted()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), EditScope.All, null,
                  It.Is<EventWrite>(w => w.KeepRepeat && w.Repeat == null), "abc123", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(new EventWriteResult(Guid.NewGuid(), [])));
        var request = ValidUpdateRequest();
        request.KeepRepeat = true;

        Assert.IsType<OkObjectResult>(await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task Update_WhenAccepted_Returns200WithWhatWasSent()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), EditScope.All, null, It.IsAny<EventWrite>(),
                  "abc123", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(new EventWriteResult(Guid.NewGuid(), [])));

        var result = await CreateController().Update(Guid.NewGuid(), ValidUpdateRequest(), CancellationToken.None);

        Assert.Equal(new EventUpdated(new SchedulingReport(null, 0)), Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Update_MapsEventMovedTo409()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<EditScope>(), It.IsAny<string?>(),
                  It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<EventWriteResult>(CalendarEventStore.EventMoved));

        var result = await CreateController().Update(Guid.NewGuid(), ValidUpdateRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Update_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<EditScope>(), It.IsAny<string?>(),
                  It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<EventWriteResult>(CalendarEventStore.NotFound));

        var result = await CreateController().Update(Guid.NewGuid(), ValidUpdateRequest(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Update_ThisNeedsInstanceId()
    {
        var request = ValidUpdateRequest();
        request.Scope = EditScope.This;
        request.InstanceId = null;

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(),
            It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_ThisAndFollowingNeedsInstanceId()
    {
        var request = ValidUpdateRequest();
        request.Scope = EditScope.ThisAndFollowing;
        request.InstanceId = null;

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(),
            It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_WithoutIfHash_Returns400()
    {
        var request = ValidUpdateRequest();
        request.IfHash = null;

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(),
            It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_WithAnInvalidBody_Returns400()
    {
        var request = ValidUpdateRequest();
        request.Start = null;

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(),
            It.IsAny<string?>(), It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Delete_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), EditScope.All, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(new EventWriteResult(Guid.NewGuid(), [])));

        var result = await CreateController().Delete(Guid.NewGuid(), EditScope.All, null, null, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), EditScope.All, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<EventWriteResult>(CalendarEventStore.NotFound));

        var result = await CreateController().Delete(Guid.NewGuid(), EditScope.All, null, null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_ThisNeedsInstanceId()
    {
        var result = await CreateController().Delete(Guid.NewGuid(), EditScope.This, null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_CallsTheHookForItsChange_AndAnswersTheReport()
    {
        var id = Guid.NewGuid();
        var change = new EventChange(id, Guid.NewGuid(), $"{id}.ics", null, "BEGIN:VCALENDAR");
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, [change])));
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), change, WriteOrigin.Webmail, It.IsNotNull<Func<CancellationToken, Task<MailAccountConnection?>>>(), "fr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulingReport("webmail", 2));
        var request = ValidRequest();
        request.Language = "fr";

        var result = await CreateController().Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedId>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal(new CreatedId(id, new SchedulingReport("webmail", 2)), created);
    }

    [Fact]
    public async Task Update_CallsTheHookOncePerChange_AndAnswers200WithTheSum()
    {
        var id = Guid.NewGuid();
        var changes = new[] { new EventChange(id, Guid.NewGuid(), "a.ics", new ReplacedVersion("old", "webmail", "h"), "new"), new EventChange(Guid.NewGuid(), Guid.NewGuid(), "b.ics", null, "split") };
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, changes)));
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulingReport("webmail", 1));

        var result = await CreateController().Update(id, ValidUpdateRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new EventUpdated(new SchedulingReport("webmail", 2)), ok.Value);
        _scheduler.Verify(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), "en", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData(null, "en")]
    public async Task Delete_CallsTheHook_WithTheLanguageOfTheQuery_AndStays204(string? query, string expected)
    {
        var id = Guid.NewGuid();
        var change = new EventChange(id, Guid.NewGuid(), "a.ics", new ReplacedVersion("old", "webmail", "h"), null);
        _store.Setup(s => s.DeleteAsync(Uid, id, EditScope.All, null, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, [change])));

        Assert.IsType<NoContentResult>(await CreateController().Delete(id, EditScope.All, null, query, CancellationToken.None));
        _scheduler.Verify(s => s.AfterWriteAsync(It.IsAny<User>(), change, WriteOrigin.Webmail, It.IsNotNull<Func<CancellationToken, Task<MailAccountConnection?>>>(), expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheSession_IsResolvedLazily_AndNullWhenTheResolverRefuses()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EventWriteResult(id, [new EventChange(id, Guid.NewGuid(), "a.ics", null, "x")])));
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure<MailAccountConnection>("no cookie"));
        Func<CancellationToken, Task<MailAccountConnection?>>? opener = null;
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<User, EventChange, WriteOrigin, Func<CancellationToken, Task<MailAccountConnection?>>?, string?, CancellationToken>((_, _, _, o, _, _) => opener = o)
            .ReturnsAsync(new SchedulingReport(null, 0));

        await CreateController().Create(ValidRequest(), CancellationToken.None);

        _connections.Verify(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(await opener!(CancellationToken.None));
    }

    // One write, several resources (a split): the credentials are decrypted once for the request.
    [Fact]
    public async Task TheSession_IsResolvedOnce_ForEveryChangeOfTheWrite()
    {
        var id = Guid.NewGuid();
        var changes = new[] { new EventChange(id, Guid.NewGuid(), "a.ics", null, "x"), new EventChange(Guid.NewGuid(), Guid.NewGuid(), "b.ics", null, "y") };
        _store.Setup(s => s.UpdateAsync(Uid, id, EditScope.All, null, It.IsAny<EventWrite>(), "abc123", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EventWriteResult(id, changes)));
        var opened = new List<MailAccountConnection?>();
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<User, EventChange, WriteOrigin, Func<CancellationToken, Task<MailAccountConnection?>>?, string?, CancellationToken>(async (_, _, _, o, _, ct) =>
            {
                opened.Add(await o!(ct));
                return new SchedulingReport("webmail", 1);
            });

        await CreateController().Update(id, ValidUpdateRequest(), CancellationToken.None);

        Assert.Equal([TestConnections.Primary("john@example.com", "pw"), TestConnections.Primary("john@example.com", "pw")], opened);
        _connections.Verify(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Décision 8: the organizer is always the primary account's identity, so its session is the only one
    // the mails may leave by; a request naming a connected account gets the queue instead.
    [Fact]
    public async Task TheSession_IsNull_WhenTheRequestNamesAConnectedAccount()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EventWriteResult(id, [new EventChange(id, Guid.NewGuid(), "a.ics", null, "x")])));
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(TestConnections.Connected(Guid.NewGuid().ToString(), "john@gmail.com", "pw")));
        Func<CancellationToken, Task<MailAccountConnection?>>? opener = null;
        _scheduler.Setup(s => s.AfterWriteAsync(It.IsAny<User>(), It.IsAny<EventChange>(), WriteOrigin.Webmail, It.IsAny<Func<CancellationToken, Task<MailAccountConnection?>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<User, EventChange, WriteOrigin, Func<CancellationToken, Task<MailAccountConnection?>>?, string?, CancellationToken>((_, _, _, o, _, _) => opener = o)
            .ReturnsAsync(new SchedulingReport(null, 0));

        await CreateController().Create(ValidRequest(), CancellationToken.None);

        Assert.Null(await opener!(CancellationToken.None));
    }
}
