using System.Text;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;
using CalendarRow = weesky.Snoopy.Microservice.Data.Preferences.Calendar;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CalDavGetTests : IAsyncLifetime
{
    /// <summary>46 UTF-8 bytes for 45 characters: the accent is what makes a Content-Length counted
    /// in characters differ from one counted in bytes.</summary>
    private const string Accented = "BEGIN:VCALENDAR\r\nSUMMARY:Adá\r\nEND:VCALENDAR\r\n";

    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task ItServesTheStoredBytesUntouched()
    {
        const string ics = "BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\nUID:u1\nEND:VEVENT\nEND:VCALENDAR\n";
        GivenEvent("a.ics", ics);

        var response = await server.SendAsync("GET", DavPaths.Event(UserId, "work", "a.ics"));

        // Line endings included: what the file was PUT as is what every other client receives, or
        // the ETag describes bytes nobody holds.
        Assert.Equal(Encoding.UTF8.GetBytes(ics), response.BodyBytes);
    }

    [Fact]
    public async Task ItAnswersTheHeadersAClientReads()
    {
        GivenEvent("a.ics", Accented, hash: "abc123",
            updatedAt: new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc));

        var response = await server.SendAsync("GET", DavPaths.Event(UserId, "work", "a.ics"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("\"abc123\"", response.Header("ETag"));
        Assert.Equal(DavHeaders.CalendarContentType, response.Header("Content-Type"));
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", response.Header("Last-Modified"));
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
        Assert.NotEqual(Accented.Length, Encoding.UTF8.GetByteCount(Accented));
        Assert.Equal("46", response.Header("Content-Length"));
    }

    [Fact]
    public async Task Head_AnswersTheSameHeadersWithNoBody()
    {
        GivenEvent("a.ics", Accented, hash: "abc123");

        var response = await server.SendAsync("HEAD", DavPaths.Event(UserId, "work", "a.ics"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("46", response.Header("Content-Length"));
        Assert.Empty(response.BodyBytes);
    }

    [Fact]
    public async Task AConditionalGetOnTheCurrentEtag_Answers304()
    {
        GivenEvent("a.ics", Accented, hash: "abc123");

        var response = await server.SendAsync("GET", DavPaths.Event(UserId, "work", "a.ics"),
            headers: new Dictionary<string, string> { ["If-None-Match"] = "\"abc123\"" });

        Assert.Equal(304, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownEvent_Answers404()
    {
        GivenCalendar();

        var response = await server.SendAsync("GET", DavPaths.Event(UserId, "work", "nowhere.ics"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AnEventOfAnUnknownCalendar_Answers404()
    {
        var response = await server.SendAsync("GET", DavPaths.Event(UserId, "nowhere", "a.ics"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AForeignUserId_Answers404()
    {
        GivenEvent("a.ics", Accented);

        var response = await server.SendAsync(
            "GET", DavPaths.Event(Guid.NewGuid(), "work", "a.ics"));

        Assert.Equal(404, response.StatusCode);
    }

    private CalendarRow GivenCalendar()
    {
        using var db = server.CreateContext();
        var existing = db.Calendars.FirstOrDefault(c => c.UserId == UserId);
        if (existing is not null) return existing;

        var row = new CalendarRow
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DavName = "work",
            DisplayName = "Work",
            Description = string.Empty,
            Color = "#336699",
            Order = 1,
            TimeZone = "Europe/Brussels",
            IsVisible = true,
        };
        db.Calendars.Add(row);
        db.SaveChanges();
        return row;
    }

    private void GivenEvent(string davName, string ics, string hash = "h1", DateTime? updatedAt = null)
    {
        var calendar = GivenCalendar();
        using var db = server.CreateContext();
        db.CalendarEvents.Add(new CalendarEvent
        {
            Id = Guid.NewGuid(),
            CalendarId = calendar.Id,
            UserId = UserId,
            Uid = Guid.NewGuid().ToString(),
            DavName = davName,
            StartsAt = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            EndsAt = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            FirstOccurrence = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc),
            LastOccurrence = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
            Transparency = "OPAQUE",
            IcsRaw = ics,
            IcsHash = hash,
            SyncSequence = 1,
            UpdatedAt = updatedAt ?? new DateTime(2026, 9, 7, 6, 0, 0, DateTimeKind.Utc),
        });
        db.SaveChanges();
    }
}
