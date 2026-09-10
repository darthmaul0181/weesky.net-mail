using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// DELETE of an event, and the one thing it must never do: bury a resource it has just refused
/// to remove — a tombstone is what tells every OTHER device the event is gone.
/// </summary>
public sealed class CalDavDeleteTests : IAsyncLifetime
{
    private DavTestServer server = null!;
    private Guid calendarId;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        calendarId = GivenCalendar("work");
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ADelete_Answers204AndLaysTheTombstoneTheReaderServes()
    {
        await GivenAnEvent("a.ics");

        var response = await Delete(Href("a.ics"));

        Assert.Equal(204, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
        Assert.Equal(404, (await server.SendAsync("GET", Href("a.ics"))).StatusCode);
        // Through the reader a sync-collection will use, at the rank the deletion took.
        using var db = server.CreateContext();
        var tombstone = Assert.Single(await new DavCalendarReader(db)
            .TombstonesAsync(calendarId, 0, ulong.MaxValue, CancellationToken.None));
        Assert.Equal("a.ics", tombstone.DavName);
        Assert.Equal(2ul, tombstone.SyncSequence);
        Assert.Equal(2ul, db.CalendarSyncStates.Single(s => s.CalendarId == calendarId).Seq);
    }

    [Fact]
    public async Task ADeleteWithAMatchingIfMatch_Succeeds()
    {
        var etag = await GivenAnEvent("a.ics");

        Assert.Equal(204, (await Delete(Href("a.ics"), ifMatch: etag)).StatusCode);
    }

    [Fact]
    public async Task AStaleIfMatch_Answers412BuriesNothingAndTakesNoRank()
    {
        await GivenAnEvent("a.ics");

        var response = await Delete(Href("a.ics"), ifMatch: "\"stale\"");

        Assert.Equal(412, response.StatusCode);
        using var db = server.CreateContext();
        Assert.Empty(db.CalendarTombstones);
        Assert.Single(db.CalendarEvents);
        Assert.Equal(1ul, db.CalendarSyncStates.Single(s => s.CalendarId == calendarId).Seq);
    }

    [Fact]
    public async Task AWeakIfMatch_IsRefused()
    {
        var etag = await GivenAnEvent("a.ics");

        Assert.Equal(412, (await Delete(Href("a.ics"), ifMatch: $"W/{etag}")).StatusCode);
    }

    [Fact]
    public async Task AnIfNoneMatchStarOnAResourceThatExists_Answers412()
    {
        await GivenAnEvent("a.ics");

        Assert.Equal(412, (await Delete(Href("a.ics"), ifNoneMatch: "*")).StatusCode);
    }

    [Fact]
    public async Task ADeleteOfWhatIsNotThere_Answers404() =>
        Assert.Equal(404, (await Delete(Href("never.ics"))).StatusCode);

    [Fact]
    public async Task ADeleteInAnUnknownCalendar_Answers404() =>
        Assert.Equal(404, (await Delete(DavPaths.Event(UserId, "nowhere", "a.ics"))).StatusCode);

    [Fact]
    public async Task AnotherUsersEvent_Answers404()
    {
        await GivenAnEvent("a.ics");

        Assert.Equal(404, (await Delete(DavPaths.Event(Guid.NewGuid(), "work", "a.ics"))).StatusCode);
        using var db = server.CreateContext();
        Assert.Single(db.CalendarEvents);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a.ics%20")]
    public async Task AnInvalidName_Answers404(string escaped) =>
        // A name the calendar will not hold designates nothing: the 404 of any other absence,
        // never PUT's 403, there being no resource here to refuse to delete.
        Assert.Equal(404, (await Delete($"{DavPaths.Calendar(UserId, "work")}{escaped}")).StatusCode);

    [Fact]
    public async Task ADeleteThatLostALockRace_Answers503WithRetryAfter()
    {
        var writer = new Mock<IDavCalendarWriter>();
        writer.Setup(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Busy, null, null, 0));
        await using var busy = await DavTestServer.StartAsync(overrides: services =>
            services.AddScoped<IDavCalendarWriter>(_ => writer.Object));
        GivenAnEventRow(busy, "a.ics");

        var response = await busy.SendAsync("DELETE", DavPaths.Event(busy.UserId, "work", "a.ics"));

        Assert.Equal(503, response.StatusCode);
        Assert.Equal("1", response.Header("Retry-After"));
    }

    private string Href(string davName) => DavPaths.Event(UserId, "work", davName);

    private async Task<string> GivenAnEvent(string davName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Href(davName));
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(CalDavPutTests.Event(Guid.NewGuid().ToString())));
        using var response = await server.Client.SendAsync(request);
        var read = await DavTestResponse.ReadAsync(response);
        Assert.Equal(201, read.StatusCode);
        return read.Header("ETag")!;
    }

    private Task<DavTestResponse> Delete(string path, string? ifMatch = null, string? ifNoneMatch = null)
    {
        var headers = new Dictionary<string, string>();
        if (ifMatch is not null) headers["If-Match"] = ifMatch;
        if (ifNoneMatch is not null) headers["If-None-Match"] = ifNoneMatch;
        return server.SendAsync("DELETE", path, headers: headers);
    }

    private Guid GivenCalendar(string davName) => GivenCalendarOn(server, davName);

    private static Guid GivenCalendarOn(DavTestServer on, string davName)
    {
        using var db = on.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(), UserId = on.UserId, DavName = davName, DisplayName = "Work",
            Description = string.Empty, Color = "#336699", Order = 1,
            TimeZone = "Europe/Brussels", IsVisible = true,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static void GivenAnEventRow(DavTestServer on, string davName)
    {
        var calendar = GivenCalendarOn(on, "work");
        using var db = on.CreateContext();
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(), CalendarId = calendar, UserId = on.UserId, Uid = "u1",
            DavName = davName, IcsRaw = "x", IcsHash = "h", SyncSequence = 1, UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }
}
