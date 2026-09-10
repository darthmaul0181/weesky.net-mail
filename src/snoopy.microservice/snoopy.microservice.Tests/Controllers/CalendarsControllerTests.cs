using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CalendarsControllerTests
{
    private static readonly Guid Uid = Guid.NewGuid();
    private const string Zone = "Europe/Brussels";

    private readonly Mock<ICalendarStore> _store = new();
    private readonly Mock<ICalendarEventStore> _events = new();

    private CalendarsController CreateController()
    {
        var controller = new CalendarsController(_store.Object, _events.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }

    private static CalendarView View(Guid? id = null, string name = "Personal", bool isDefault = true) =>
        new(id ?? Guid.NewGuid(), isDefault ? "default" : Guid.NewGuid().ToString(), name, string.Empty,
            "#3b82c4", 0, Zone, true, isDefault);

    [Fact]
    public async Task List_EnsuresDefaultWithTheBrowserZone()
    {
        _store.Setup(s => s.EnsureDefaultAsync(Uid, Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(View());
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([View()]);

        var result = await CreateController().List(Zone, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(Assert.IsType<CalendarListResponse>(ok.Value).Calendars);
        _store.Verify(s => s.EnsureDefaultAsync(Uid, Zone, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_RefusesUnknownZone()
    {
        var result = await CreateController().List("Nowhere/Land", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.EnsureDefaultAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WhenAccepted_Returns201WithTheId()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<CalendarWrite>(), Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(id));

        var result = await CreateController().Create(
            Zone, new CalendarRequest { DisplayName = "Work" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Equal(id, Assert.IsType<CreatedId>(obj.Value).Id);
    }

    [Fact]
    public async Task Create_RefusesUnknownZone()
    {
        var result = await CreateController().Create(
            "Nowhere/Land", new CalendarRequest { DisplayName = "Work" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithoutDisplayName_Returns400()
    {
        var result = await CreateController().Create(Zone, new CalendarRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CalendarWrite>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_AtTheCap_Returns400()
    {
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<CalendarWrite>(), Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<Guid>(CalendarStore.CapReached));

        var result = await CreateController().Create(
            Zone, new CalendarRequest { DisplayName = "Work" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<CalendarWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().Update(
            Guid.NewGuid(), new CalendarRequest { DisplayName = "Work" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Update_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<CalendarWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarStore.NotFound));

        var result = await CreateController().Update(
            Guid.NewGuid(), new CalendarRequest { DisplayName = "Work" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Update_WithBadColour_Returns400()
    {
        _store.Setup(s => s.UpdateAsync(Uid, It.IsAny<Guid>(), It.IsAny<CalendarWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarStore.BadColour));

        var result = await CreateController().Update(
            Guid.NewGuid(), new CalendarRequest { DisplayName = "Work", Color = "red" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Update_WithoutDisplayName_Returns400()
    {
        var result = await CreateController().Update(Guid.NewGuid(), new CalendarRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetVisible_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.SetVisibleAsync(Uid, It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().SetVisible(
            Guid.NewGuid(), new CalendarVisibleRequest { Visible = false }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task SetVisible_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.SetVisibleAsync(Uid, It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarStore.NotFound));

        var result = await CreateController().SetVisible(
            Guid.NewGuid(), new CalendarVisibleRequest { Visible = true }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        Assert.IsType<NoContentResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_DefaultIs400()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarStore.NotDeletable));

        Assert.IsType<BadRequestObjectResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.DeleteAsync(Uid, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(CalendarStore.NotFound));

        Assert.IsType<NotFoundObjectResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    private static IFormFile IcsFile(string text, string? mediaType = "text/calendar")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "calendar.ics");
        if (mediaType != null) file.Headers = new HeaderDictionary { ["Content-Type"] = mediaType };
        return file;
    }

    [Fact]
    public async Task Import_RefusesWrongMediaType_And_Export_IsTextCalendar()
    {
        var badMediaType = await CreateController().Import(
            Guid.NewGuid(), IcsFile("BEGIN:VCALENDAR", "text/plain"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badMediaType.Result);
        _events.Verify(e => e.ImportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var id = Guid.NewGuid();
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([View(id: id)]);
        _events.Setup(e => e.ExportAsync(Uid, id, It.IsAny<CancellationToken>())).ReturnsAsync("BEGIN:VCALENDAR\r\n");

        var file = Assert.IsType<FileContentResult>(await CreateController().Export(id, CancellationToken.None));
        Assert.Equal("text/calendar", file.ContentType);
        Assert.EndsWith(".ics", file.FileDownloadName);
    }

    [Fact]
    public async Task Import_Returns200WithTheReport()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([View(id: id)]);
        _events.Setup(e => e.ImportAsync(Uid, id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CalendarImportOutcome(2, 1, 0, 0, 0, []));

        var result = await CreateController().Import(id, IcsFile("BEGIN:VCALENDAR"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<CalendarImportReport>(ok.Value);
        Assert.Equal(2, report.Created);
        Assert.Equal(1, report.Replaced);
    }

    [Fact]
    public async Task Import_WhenCalendarIsNotOwned_Returns404()
    {
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await CreateController().Import(Guid.NewGuid(), IcsFile("BEGIN:VCALENDAR"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _events.Verify(e => e.ImportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Import_WithoutAFile_Returns400()
    {
        var result = await CreateController().Import(Guid.NewGuid(), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ImportAsNew_CreatesThenImports()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<CalendarWrite>(), Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(id));
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>()))
              .ReturnsAsync([View(id: id, name: "Holidays", isDefault: false)]);
        _events.Setup(e => e.ImportAsync(Uid, id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new CalendarImportOutcome(3, 0, 0, 0, 0, []));

        var result = await CreateController().ImportAsNew(
            Zone, "Holidays", "#3b82c4", IcsFile("BEGIN:VCALENDAR"), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
        var body = Assert.IsType<CalendarImportResponse>(obj.Value);
        Assert.Equal(id, body.Calendar.Id);
        Assert.Equal("Holidays", body.Calendar.DisplayName);
        Assert.Equal(3, body.Report.Created);
    }

    [Fact]
    public async Task ImportAsNew_RefusesAtTheCapWithoutReadingTheFile()
    {
        _store.Setup(s => s.CreateAsync(Uid, It.IsAny<CalendarWrite>(), Zone, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<Guid>(CalendarStore.CapReached));

        var result = await CreateController().ImportAsNew(
            Zone, "Holidays", null, IcsFile("BEGIN:VCALENDAR"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _events.Verify(e => e.ImportAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsNew_NeedsADisplayName()
    {
        var result = await CreateController().ImportAsNew(
            Zone, "  ", null, IcsFile("BEGIN:VCALENDAR"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CalendarWrite>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsNew_RefusesAnUnknownZoneAndAWrongMediaType()
    {
        Assert.IsType<BadRequestObjectResult>((await CreateController().ImportAsNew(
            "Nowhere/Land", "Holidays", null, IcsFile("BEGIN:VCALENDAR"), CancellationToken.None)).Result);

        Assert.IsType<BadRequestObjectResult>((await CreateController().ImportAsNew(
            Zone, "Holidays", null, IcsFile("BEGIN:VCALENDAR", "text/plain"), CancellationToken.None)).Result);

        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CalendarWrite>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_WhenCalendarIsNotOwned_Returns404()
    {
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await CreateController().Export(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
