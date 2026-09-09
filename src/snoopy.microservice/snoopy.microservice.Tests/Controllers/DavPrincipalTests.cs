using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// The two switches at the edge, and the shapes they gate. Its own file rather than a case added to
/// <c>CardDav*Tests</c>: that suite is the address book's, and nothing there may move.
/// </summary>
public sealed class DavPrincipalTests
{
    private const string PropfindBody =
        """<?xml version="1.0"?><propfind xmlns="DAV:"><prop><resourcetype/></prop></propfind>""";

    private static readonly string HomeSetsBody = PropBody(
        DavXml.CardDav + "addressbook-home-set", DavXml.CalDav + "calendar-home-set");

    [Fact]
    public async Task ASwitchedOffProtocol_Is403WithNoBodyOnItsOwnTree()
    {
        await using var server = await DavTestServer.StartAsync(cardDav: false);

        var response = await server.PropfindAsync(
            DavPaths.Home(server.UserId), depth: "0", PropfindBody);

        // No body and no precondition: a switched-off service says nothing at all, not even which
        // guid it holds — the refusal precedes the ownership check.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task ASwitchedOffProtocol_Is403EvenOnAForeignUserId()
    {
        await using var server = await DavTestServer.StartAsync(cardDav: false);

        var response = await server.PropfindAsync(
            DavPaths.Home(Guid.NewGuid()), depth: "0", PropfindBody);

        // The 404 of a foreign {userId} would confirm that the switched-off account exists.
        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task ThePrincipal_StillAnswersWhileEitherServiceIsOn()
    {
        await using var server = await DavTestServer.StartAsync(cardDav: false);

        var response = await server.PropfindAsync(
            DavPaths.Principal(server.UserId), depth: "0", PropfindBody);

        // Where a client discovers both home-sets: a 403 here would stop the service that IS on
        // from ever being found.
        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task ThePrincipal_Is403WhenBothServicesAreOff()
    {
        await using var server = await DavTestServer.StartAsync(cardDav: false, calDav: false);

        var response = await server.PropfindAsync(
            DavPaths.Principal(server.UserId), depth: "0", PropfindBody);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task Options_AnswersEverywhereWhateverTheSwitches()
    {
        await using var server = await DavTestServer.StartAsync(cardDav: false, calDav: false);

        var response = await server.SendAsync("OPTIONS", DavPaths.Home(server.UserId));

        // Anonymous, and answered off the URL shape alone: a client asks what the server can do
        // before it holds any credentials, so it cannot depend on a switch.
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(DavHeaders.ComplianceClasses, response.Header("DAV"));
    }

    [Fact]
    public async Task ThePrincipal_AnnouncesBothHomeSetsWhenBothServicesAreOn()
    {
        await using var server = await DavTestServer.StartAsync();

        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0",
            HomeSetsBody);

        Assert.Equal(DavPaths.Home(server.UserId),
            HrefOf(response, DavXml.CardDav + "addressbook-home-set"));
        Assert.Equal(DavPaths.CalendarHome(server.UserId),
            HrefOf(response, DavXml.CalDav + "calendar-home-set"));
    }

    [Fact]
    public async Task ASwitchedOffService_IsNotAnnouncedOnThePrincipal()
    {
        await using var server = await DavTestServer.StartAsync(cardDav: false);

        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0",
            HomeSetsBody);

        // Announced, the client follows the href to read a 403 on every cycle; the 404 propstat is
        // the honest answer for a service this account does not run.
        Assert.Contains("404", StatusOf(response, DavXml.CardDav + "addressbook-home-set"));
        Assert.Contains("200", StatusOf(response, DavXml.CalDav + "calendar-home-set"));
    }

    [Fact]
    public async Task TheCalendarUserAddressSet_CarriesEveryAddressTheAccountAnswersTo()
    {
        await using var server = await DavTestServer.StartAsync(
            email: "Someone@weesky.be",
            overrides: services => services
                .WithDomains("weesky.be", "weesky.net")
                .WithSendingIdentities("someone@weesky.be", "Sales@weesky.net", "me@example.org"));

        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0",
            PropBody(DavXml.CalDav + "calendar-user-address-set"));

        // The primary first, then the same name on the account's other domains, then the sending
        // identities — lower-cased, without duplicates, in that order: this is how a client
        // recognises itself as a participant of an invitation it received on an alias.
        Assert.Equal(
            ["mailto:someone@weesky.be", "mailto:someone@weesky.net", "mailto:sales@weesky.net", "mailto:me@example.org"],
            XDocument.Parse(response.Body)
                .Descendants(DavXml.CalDav + "calendar-user-address-set")
                .Single().Elements(DavXml.Href).Select(href => href.Value));
    }

    [Fact]
    public async Task ThePrincipal_AnswersWithoutItsDomainsWhenThePlatformCannotSayThem()
    {
        await using var server = await DavTestServer.StartAsync(
            overrides: services => services.WithoutAccountInfo().WithSendingIdentities());

        var response = await server.PropfindAsync(DavPaths.Principal(server.UserId), "0",
            PropBody(DavXml.CalDav + "calendar-user-address-set"));

        Assert.Equal([$"mailto:{server.Email}"], XDocument.Parse(response.Body)
            .Descendants(DavXml.CalDav + "calendar-user-address-set")
            .Single().Elements(DavXml.Href).Select(href => href.Value));
    }

    [Fact]
    public async Task Proppatch_RemovingAPropertyThatWasNeverSet_Answers200()
    {
        // The shared refusal path (DavControllerBase.ProppatchAsync) carries RFC 4918 § 14.23 too:
        // removing what this shape does not carry at all is not an error.
        await using var server = await DavTestServer.StartAsync();

        var response = await server.SendAsync("PROPPATCH", DavPaths.Principal(server.UserId),
            RemoveBody(Dead("details")));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains("200", StatusOf(response, Dead("details")));
    }

    [Fact]
    public async Task Proppatch_RemovingTheCalendarUserAddressSet_IsStillRefused()
    {
        // Its factory reads the account's domains, gathered here for the same reason PROPFIND
        // gathers them (AddressesOrNullAsync): a remove of a live property must not read as
        // absent merely because nothing had fetched its value yet.
        await using var server = await DavTestServer.StartAsync(
            email: "Someone@weesky.be",
            overrides: services => services
                .WithDomains("weesky.be", "weesky.net")
                .WithSendingIdentities("someone@weesky.be", "Sales@weesky.net", "me@example.org"));

        var response = await server.SendAsync("PROPPATCH", DavPaths.Principal(server.UserId),
            RemoveBody(DavXml.CalDav + "calendar-user-address-set"));

        Assert.Contains("403", StatusOf(response, DavXml.CalDav + "calendar-user-address-set"));
    }

    [Fact]
    public async Task AnExpandPropertyNestedOnTheCalendarHomeSet_Resolves()
    {
        await using var server = await DavTestServer.StartAsync();
        var body = """
            <D:expand-property xmlns:D="DAV:">
              <D:property name="calendar-home-set" namespace="urn:ietf:params:xml:ns:caldav">
                <D:property name="displayname"/>
              </D:property>
            </D:expand-property>
            """;

        var response = await server.SendAsync("REPORT", DavPaths.Principal(server.UserId), body);

        // The home-set is the one href of the principal that leaves the principal's own tables:
        // the controller has to route the nested kind to the calendar tables, or the resolution is
        // a KeyNotFoundException — a 500 the client retries for ever.
        Assert.Equal(207, response.StatusCode);
        Assert.Contains(DavPaths.CalendarHome(server.UserId),
            XDocument.Parse(response.Body).Descendants(DavXml.Href).Select(href => href.Value));
        Assert.Equal("Calendars", XDocument.Parse(response.Body)
            .Descendants(DavXml.Dav + "displayname").Single().Value);
    }

    [Fact]
    public void ThePrincipalTable_AnswersAKindItDoesNotHoldAsA404Propstat()
    {
        // The shape an expand-property nests into from a home-set the table does not carry. An
        // indexer here would make the next href property added to the principal a 500, and
        // CalDavNoFiveHundredTests only ever catches what it exercises.
        var request = DavPropertyRequest.Parse(XDocument.Parse(PropfindBody));
        var resource = new DavResourceContext(
            DavResourceKind.CalendarHome, Guid.NewGuid(), "someone@weesky.be", null, null);

        var (found, missing) = DavPrincipalProperties.Resolve(request, resource);

        Assert.Empty(found);
        Assert.Equal(DavXml.Dav + "resourcetype", Assert.Single(missing));
    }

    private static string PropBody(params XName[] names) =>
        new XElement(DavXml.Dav + "propfind",
            new XElement(DavXml.Prop, names.Select(name => new XElement(name)))).ToString();

    private static string RemoveBody(XName name) =>
        new XElement(DavXml.Dav + "propertyupdate",
            new XElement(DavXml.Dav + "remove", new XElement(DavXml.Prop, new XElement(name)))).ToString();

    /// <summary>A name in a namespace that is neither DAV:, CalDAV nor CardDAV's — a property this
    /// server never stores, whatever the client calls it.</summary>
    private static XName Dead(string localName) => XNamespace.Get("http://example.com/ns/") + localName;

    private static string? HrefOf(DavTestResponse response, XName property) =>
        XDocument.Parse(response.Body).Descendants(property)
            .SelectMany(element => element.Elements(DavXml.Href)).SingleOrDefault()?.Value;

    private static string StatusOf(DavTestResponse response, XName property) =>
        XDocument.Parse(response.Body).Descendants(DavXml.PropStat)
            .Single(propstat => propstat.Element(DavXml.Prop)!.Elements()
                .Any(element => element.Name == property))
            .Element(DavXml.Status)!.Value;
}
