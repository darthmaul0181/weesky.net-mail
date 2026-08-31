using weesky.Snoopy.Microservice.Services.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// What is left of the HTTP surface once PROPFIND, GET and REPORT are served: OPTIONS, the 405 a
/// method we do not serve earns, and the 308 that canonicalises a collection URL.
/// </summary>
public sealed class CardDavSurfaceTests : IAsyncLifetime
{
    private DavTestServer server = null!;

    private Guid UserId => server.UserId;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Fact]
    public async Task Options_AnnouncesTheComplianceClasses()
    {
        var response = await server.SendAsync("OPTIONS", DavPaths.Collection(UserId));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
    }

    [Fact]
    public async Task Options_AnswersUnauthenticatedToo()
    {
        var response = await server.SendUnauthenticated("OPTIONS", DavPaths.Collection(UserId));

        // A client asks for capabilities before it has credentials.
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
    }

    [Theory]
    [InlineData("PROPFIND")]
    [InlineData("PROPPATCH")]
    [InlineData("REPORT")]
    [InlineData("GET")]
    [InlineData("MKCOL")]
    public async Task EverythingButOptions_StillDemandsCredentials(string method)
    {
        var response = await server.SendUnauthenticated(method, DavPaths.Collection(UserId));

        // [AllowAnonymous] sits on the three OPTIONS actions and on no other: were it on the class,
        // the whole book would be readable without a password.
        Assert.Equal(401, response.StatusCode);
    }

    [Fact]
    public async Task Options_OnACollection_AllowsTheFiveCollectionMethods()
    {
        var response = await server.SendAsync("OPTIONS", DavPaths.Collection(UserId));

        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task Options_OnACard_NamesHeadAndReport()
    {
        // No card is seeded: answering anonymously means answering off the URL shape alone, so a
        // capabilities question can never confirm that a card exists.
        var response = await server.SendAsync("OPTIONS", DavPaths.Card(UserId, "a.vcf"));

        // HEAD because HTTP requires it as soon as GET exists, and a client that does not see it
        // there does not try it. REPORT because multiget and query answer on a card - omitting it
        // would make the header say the opposite of what the method answers.
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.CardAllow, response.Header("Allow"));
    }

    [Theory]
    [InlineData("MKCOL")]
    [InlineData("MKCALENDAR")]
    [InlineData("COPY")]
    [InlineData("MOVE")]
    [InlineData("ACL")]
    [InlineData("LOCK")]
    [InlineData("UNLOCK")]
    public async Task AMethodWeDoNotServe_Answers405WithAllow(string method)
    {
        var response = await server.SendAsync(method, DavPaths.Collection(UserId));

        // LOCK and UNLOCK are in the list although DAV: 1, 3 already announces no locks: the
        // announcement says what we can do, the 405 says what we answer when a client has not read
        // it - and without it routing would give a 404, i.e. "this card does not exist" on a card
        // that does.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task AMethodWeDoNotServe_OnACard_NamesTheCardsVerbs()
    {
        var response = await server.SendAsync("MKCOL", DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CardAllow, response.Header("Allow"));
    }

    [Theory]
    [InlineData("PUT")]
    public async Task AWriteOnTheCollection_Answers405(string method)
    {
        var response = await server.SendAsync(method, DavPaths.Collection(UserId));

        // The route only binds PUT on {name}, a segment the collection URL - ending in a slash -
        // does not present: routing would give an accidental 404. DELETE of the collection is
        // served (decision 3: it empties the book) and answers here of its own accord.
        Assert.Equal(405, response.StatusCode);

        // The Allow, not the status: routing answers 405 here on its own, so a test asserting the
        // status alone stays green with both catch-alls deleted.
        Assert.Equal(DavHeaders.CollectionAllow, response.Header("Allow"));
    }

    [Theory]
    [InlineData("/dav")]
    [InlineData("/dav/principals/{0}/")]
    [InlineData("/dav/addressbooks/{0}/")]
    public async Task AGetOnAResourceThatOnlyAnswersPropfind_Answers405WithAllow(string template)
    {
        var response = await server.SendAsync("GET", string.Format(template, UserId));

        // Task 7 gave the collection its Allow; these three answered a routing 405 carrying none,
        // which tells a client the request failed but never which verb would have worked. All
        // three are home, principal or root, never the collection, so the Allow they carry is the
        // home's — DELETE only the address book serves (4d decision 3).
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task Options_OnTheBareRoot_AnswersTheCapabilitiesToo()
    {
        // A client given the bare host tries "/" as much as the well-known, and nothing there makes
        // it give up before it ever reaches the well-known.
        var response = await server.SendAsync("OPTIONS", "/");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
        Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task Options_OnTheHome_DoesNotAnnounceDelete()
    {
        var response = await server.SendAsync("OPTIONS", DavPaths.Home(UserId));

        // The home's Allow must not gain the collection's DELETE: announcing a verb that answers
        // 405 is the exact lie the routing Allow tells and these actions exist to avoid.
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task TheBareRoot_CarriesNoCatchAll()
    {
        var response = await server.SendAsync("POST", "/");

        // Deliberately absent: bound to no verb, a catch-all at "/" would swallow every unrouted
        // method of the WHOLE API, not merely the DAV surface. What answers is routing's own 405,
        // whose Allow is the union of the two verbs bound at "/" - never ours.
        Assert.Equal(405, response.StatusCode);
        Assert.NotEqual(DavHeaders.CollectionAllow, response.Header("Allow"));
        Assert.NotEqual(DavHeaders.HomeAllow, response.Header("Allow"));
    }

    [Fact]
    public async Task ACollectionWithoutItsTrailingSlash_Answers308()
    {
        var response = await server.SendAsync("PROPFIND", $"/dav/addressbooks/{UserId}/default");

        // 308 and not the 301 sabre and Radicale use: a 301 lets the client replay as GET, which
        // bare OkHttp does for every verb but PROPFIND - a redirected REPORT would lose its method
        // and its body.
        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Collection(UserId), response.Header("Location"));
    }

    [Fact]
    public async Task ADeleteOfACollectionWithoutItsTrailingSlash_Answers308()
    {
        var response = await server.SendAsync("DELETE", $"/dav/addressbooks/{UserId}/default");

        // The redirect must hold for the destructive verb too, not only for PROPFIND/REPORT: a
        // client that DELETEs the un-canonical spelling must be redirected onto the collection that
        // answers, never onto a routing 404 or a 405 that says nothing moved.
        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Collection(UserId), response.Header("Location"));
    }

    [Fact]
    public async Task AHomeWithoutItsTrailingSlash_Answers308Too()
    {
        var response = await server.SendAsync("PROPFIND", $"/dav/addressbooks/{UserId}");

        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Home(UserId), response.Header("Location"));
    }

    [Fact]
    public async Task APrincipalWithoutItsTrailingSlash_Answers308Too()
    {
        // The third URL DavPaths writes with a slash and DavPaths.Parse reads as nothing without
        // one: leaving it out would make the rule hold for two of three collection-shaped URLs.
        var response = await server.SendAsync("PROPFIND", $"/dav/principals/{UserId}");

        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Principal(UserId), response.Header("Location"));
    }

    [Fact]
    public async Task AReportOnACollectionWithoutItsTrailingSlash_IsRedirectedToo()
    {
        var response = await server.SendAsync("REPORT", $"/dav/addressbooks/{UserId}/default",
            "<C:addressbook-multiget xmlns:C=\"urn:ietf:params:xml:ns:carddav\"/>");

        // The verb the 308 exists for: a 301 here would let the client replay a bodiless GET.
        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Collection(UserId), response.Header("Location"));
    }

    [Fact]
    public async Task ACollectionWithItsSlash_IsNotRedirected()
    {
        // The canonical spelling must still be served, never redirected onto itself.
        var response = await server.SendAsync("PROPFIND", DavPaths.Collection(UserId), depth: "0");

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task AForeignCollectionWithoutItsSlash_Answers404RatherThanARedirect()
    {
        // Ownership is judged first: a redirect would confirm the book of a user the caller is not.
        var response = await server.SendAsync("PROPFIND", $"/dav/addressbooks/{Guid.NewGuid()}/default");

        Assert.Equal(404, response.StatusCode);
    }
}
