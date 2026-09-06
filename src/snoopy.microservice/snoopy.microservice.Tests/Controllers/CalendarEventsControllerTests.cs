using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
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

    private CalendarEventsController CreateController()
    {
        var controller = new CalendarEventsController(_store.Object);
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
    public async Task Create_WhenAccepted_Returns201WithTheId()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(id));

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
              .ReturnsAsync(Result.Failure<Guid>(CalendarStore.NotFound));

        var result = await CreateController().Create(ValidRequest(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_AtTheCap_Returns400()
    {
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<EventWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<Guid>(CalendarEventStore.CapReached));

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
              .ReturnsAsync(Result.Success());
        var request = ValidUpdateRequest();
        request.KeepRepeat = true;

        Assert.IsType<NoContentResult>(await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task Update_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), EditScope.All, null, It.IsAny<EventWrite>(),
                  "abc123", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().Update(Guid.NewGuid(), ValidUpdateRequest(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Update_MapsEventMovedTo409()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<EditScope>(), It.IsAny<string?>(),
                  It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarEventStore.EventMoved));

        var result = await CreateController().Update(Guid.NewGuid(), ValidUpdateRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Update_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<EditScope>(), It.IsAny<string?>(),
                  It.IsAny<EventWrite>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarEventStore.NotFound));

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
              .ReturnsAsync(Result.Success());

        var result = await CreateController().Delete(Guid.NewGuid(), EditScope.All, null, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), EditScope.All, null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarEventStore.NotFound));

        var result = await CreateController().Delete(Guid.NewGuid(), EditScope.All, null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_ThisNeedsInstanceId()
    {
        var result = await CreateController().Delete(Guid.NewGuid(), EditScope.This, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EditScope>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
