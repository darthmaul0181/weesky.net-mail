using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>Décision 9's second door: every accepted PUT and DELETE of a device hands the hook the
/// file before and the file after, and a refused one hands it nothing.</summary>
public sealed class CalDavSchedulingTests : IAsyncLifetime
{
    private readonly RecordingInvitationScheduler recorder = new();
    private DavTestServer server = null!;

    public async Task InitializeAsync()
    {
        server = await DavTestServer.StartAsync(overrides: services => services.AddSingleton<IInvitationScheduler>(recorder));
        CalDavDeleteTests.GivenCalendarOn(server, "work");
    }

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    private string Href(string davName) => DavPaths.Event(server.UserId, "work", davName);

    private Task<DavTestResponse> Put(string path, string body, string? ifMatch = null) =>
        server.SendAsync("PUT", path, body, contentType: "text/calendar",
            headers: ifMatch is null ? null : new Dictionary<string, string> { ["If-Match"] = ifMatch });

    [Fact]
    public async Task Put_ThenPut_ThenDelete_CallTheHook_WithBeforeAndAfter()
    {
        var first = InvitationParserTests.Fixture("webmail-invited");
        var second = first.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var url = Href("web.ics");

        Assert.Equal(201, (await Put(url, first)).StatusCode);
        Assert.Equal(204, (await Put(url, second)).StatusCode);
        Assert.Equal(204, (await server.SendAsync("DELETE", url)).StatusCode);

        Assert.Equal(3, recorder.Calls.Count);
        Assert.All(recorder.Calls, c => Assert.Equal((WriteOrigin.Device, false, (string?)null), (c.Origin, c.HasSession, c.Language)));
        Assert.All(recorder.Calls, c => Assert.Equal("web.ics", c.Change.DavName));
        Assert.Null(recorder.Calls[0].Change.Before);
        Assert.Equal(first, recorder.Calls[0].Change.After);
        Assert.Equal(first, recorder.Calls[1].Change.Before!.Ics);
        Assert.Equal(second, recorder.Calls[1].Change.After);
        Assert.Equal(second, recorder.Calls[2].Change.Before!.Ics);
        Assert.Null(recorder.Calls[2].Change.After);
    }

    [Fact]
    public async Task ARefusedPut_DoesNotCallTheHook()
    {
        var url = Href("web.ics");
        await Put(url, InvitationParserTests.Fixture("webmail-invited"));

        var refused = await Put(url, InvitationParserTests.Fixture("webmail-invited"), ifMatch: "\"stale\"");

        Assert.Equal(412, refused.StatusCode);
        Assert.Single(recorder.Calls);
    }

    [Fact]
    public async Task ARefusedDelete_DoesNotCallTheHook()
    {
        var url = Href("web.ics");
        await Put(url, InvitationParserTests.Fixture("webmail-invited"));

        var refused = await server.SendAsync("DELETE", url, headers: new Dictionary<string, string> { ["If-Match"] = "\"stale\"" });

        Assert.Equal(412, refused.StatusCode);
        Assert.Single(recorder.Calls);
    }
}
