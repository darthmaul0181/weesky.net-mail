using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class CalendarsControllerBirthdaysTests
{
    private static readonly Guid Uid = Guid.NewGuid();
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Zone = "Europe/Brussels";

    private readonly Mock<ICalendarStore> _store = new();
    private readonly Mock<ICalendarEventStore> _events = new();

    private CalendarsController CreateController() => new(_store.Object, _events.Object)
    {
        ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid)
    };

    private static CalendarView Birthdays(Guid id) =>
        new(id, "birthdays", "Birthdays", string.Empty, "#be185d", 1, Zone, true, false,
            CalendarKinds.Birthdays, BirthdayReminders.SameDay);

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData(null, "en")]
    public async Task List_ensures_the_birthdays_calendar_in_the_requested_language(string? lang, string expected)
    {
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        Assert.IsType<OkObjectResult>((await CreateController().List(Zone, lang, None)).Result);

        _store.Verify(s => s.EnsureBirthdaysAsync(Uid, Zone, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_switch_refuses_an_unknown_zone()
    {
        var result = await CreateController().SetBirthdays("Nowhere/Land", "fr", new BirthdaysToggleRequest { Enabled = true }, None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_switch_turned_off_answers_204()
    {
        var result = await CreateController().SetBirthdays(Zone, null, new BirthdaysToggleRequest { Enabled = false }, None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.SetBirthdaysEnabledAsync(Uid, false, Zone, "en", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Import_into_birthdays_is_403()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([Birthdays(id)]);

        var result = await CreateController().Import(id, CalendarsControllerTests.IcsFile("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n"), None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
        _events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Deleting_birthdays_is_400_like_default()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.DeleteAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(CalendarStore.ReadOnly));

        Assert.IsType<BadRequestObjectResult>(await CreateController().Delete(id, None));
    }

    [Fact]
    public async Task Update_passes_the_reminder()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.UpdateAsync(Uid, id, It.IsAny<CalendarWrite>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var result = await CreateController().Update(id,
            new CalendarRequest { DisplayName = "Birthdays", BirthdayReminder = BirthdayReminders.DayBefore }, None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.UpdateAsync(Uid, id, It.Is<CalendarWrite>(w => w.BirthdayReminder == BirthdayReminders.DayBefore),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
