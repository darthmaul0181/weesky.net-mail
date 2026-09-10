using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// DELETE on a collection (§ 11): a secondary calendar goes, and <c>default</c> is emptied instead
/// — a user with no calendar has nowhere to write.
/// </summary>
public sealed class CalDavDeleteCollectionTests : IAsyncLifetime
{
    private static readonly Guid Epoch = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private DavTestServer server = null!;
    private Guid defaultId;
    private Guid workId;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync();
        defaultId = GivenCalendar(CalendarStore.DefaultDavName, 0);
        workId = GivenCalendar("work", 1);
        GivenEvents(defaultId, "a.ics", "b.ics");
        GivenEvents(workId, "c.ics");
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task TheDefaultCalendar_IsEmptiedAndKept()
    {
        var response = await server.SendAsync(
            "DELETE", DavPaths.Calendar(UserId, CalendarStore.DefaultDavName));

        Assert.Equal(204, response.StatusCode);
        using var db = server.CreateContext();
        Assert.NotNull(db.Calendars.Find(defaultId));
        Assert.Empty(db.CalendarEvents.Where(e => e.CalendarId == defaultId));
        // A tombstone per resource, because the collection survives: every other device learns
        // each one is gone by name rather than losing the lot.
        Assert.Equal(["a.ics", "b.ics"],
            db.CalendarTombstones.Where(t => t.CalendarId == defaultId)
                .Select(t => t.DavName).OrderBy(name => name).ToList());
        Assert.Equal(2, db.CalendarRevisions.Count(r => r.CalendarId == defaultId));
    }

    [Fact]
    public async Task TheDefaultCalendarStillListedAfterwards_AnswersItsPropfind()
    {
        Assert.Equal(204, (await server.SendAsync(
            "DELETE", DavPaths.Calendar(UserId, CalendarStore.DefaultDavName))).StatusCode);

        var listed = await server.PropfindAsync(
            DavPaths.Calendar(UserId, CalendarStore.DefaultDavName), "0", null);

        Assert.Equal(207, listed.StatusCode);
    }

    [Fact]
    public async Task AnEmptyDefaultCalendar_IsStill204()
    {
        Assert.Equal(204, (await server.SendAsync(
            "DELETE", DavPaths.Calendar(UserId, CalendarStore.DefaultDavName))).StatusCode);

        var again = await server.SendAsync(
            "DELETE", DavPaths.Calendar(UserId, CalendarStore.DefaultDavName));

        Assert.Equal(204, again.StatusCode);
    }

    [Fact]
    public async Task ASecondaryCalendar_IsRemovedAndThen404s()
    {
        var response = await server.SendAsync("DELETE", DavPaths.Calendar(UserId, "work"));

        Assert.Equal(204, response.StatusCode);
        Assert.Equal(404,
            (await server.PropfindAsync(DavPaths.Calendar(UserId, "work"), "0", null)).StatusCode);
        using var db = server.CreateContext();
        Assert.Null(db.Calendars.Find(workId));
        Assert.Null(await db.CalendarSyncStates.FindAsync(workId));
        // The whole collection goes, so nothing is buried name by name — a client that loses the
        // collection loses everything under it.
        Assert.Empty(db.CalendarTombstones.Where(t => t.CalendarId == workId));
        Assert.Single(db.CalendarRevisions.Where(r => r.CalendarId == workId));
    }

    [Fact]
    public async Task AnUnknownCalendar_Answers404()
    {
        var response = await server.SendAsync("DELETE", DavPaths.Calendar(UserId, "nope"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AnotherAccountsCalendar_Answers404AndTouchesNothing()
    {
        var stranger = Guid.NewGuid();
        GivenCalendar(CalendarStore.DefaultDavName, 0, stranger);

        var response = await server.SendAsync(
            "DELETE", DavPaths.Calendar(stranger, CalendarStore.DefaultDavName));

        // The ownership check of the frame, before the collection is even looked up: telling a
        // foreign id apart from an unknown one would confirm it exists.
        Assert.Equal(404, response.StatusCode);
        using var db = server.CreateContext();
        Assert.Single(db.Calendars.Where(c => c.UserId == stranger));
    }

    [Fact]
    public async Task TheHome_Answers405WithItsOwnAllow()
    {
        var response = await server.SendAsync("DELETE", DavPaths.CalendarHome(UserId));

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarHomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task TheCollectionOfHomes_Answers405()
    {
        var response = await server.SendAsync("DELETE", DavPaths.CalendarCollection);

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task AStaleIfMatch_IsNotConsultedOnACollection()
    {
        var response = await server.SendAsync("DELETE", DavPaths.Calendar(UserId, "work"),
            headers: new Dictionary<string, string> { ["If-Match"] = "\"nothing\"" });

        // A collection carries no entity tag to compare a header against (§ 11).
        Assert.Equal(204, response.StatusCode);
    }

    private Guid GivenCalendar(string davName, int order, Guid? owner = null)
    {
        using var db = server.CreateContext();
        var row = new CalendarRow
        {
            Id = Guid.NewGuid(),
            UserId = owner ?? UserId,
            DavName = davName,
            DisplayName = davName,
            Description = string.Empty,
            Color = "#336699",
            Order = order,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        };
        db.Calendars.Add(row);
        db.CalendarSyncStates.Add(new CalendarSyncState
        {
            CalendarId = row.Id, Epoch = Epoch, Seq = 3, PrunedBelow = 0,
        });
        db.SaveChanges();
        return row.Id;
    }

    private void GivenEvents(Guid calendarId, params string[] davNames)
    {
        using var db = server.CreateContext();
        foreach (var davName in davNames)
        {
            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = Guid.NewGuid(),
                CalendarId = calendarId,
                UserId = UserId,
                Uid = davName,
                DavName = davName,
                StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
                EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
                FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
                LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
                IcsRaw = "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n",
                IcsHash = $"hash-{davName}",
                SyncSequence = 1,
                UpdatedAt = new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
            });
        }

        db.SaveChanges();
    }
}
