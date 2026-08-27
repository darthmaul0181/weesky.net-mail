using System.Text;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.CardDav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CardDav;

public sealed class DavPropertiesTests
{
    // Every fixture value is distinct from every other one: a fixture where two fields share a
    // value leaves half of what the assertions claim to pin actually unpinned.
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Uid = "urn:uuid:44444444-4444-4444-4444-444444444444";
    private const string DavName = "ada.vcf";
    private const string PrincipalAddress = "ada@weesky.be";
    private static readonly SyncState State = new(Epoch, 42, 7);

    [Fact]
    public void TheContentLength_CountsBytesAndNotCharacters()
    {
        var card = CardWith("BEGIN:VCARD\r\nFN:Ada Éléonore\r\nEND:VCARD\r\n");

        var value = Resolve(card, DavXml.Dav + "getcontentlength");

        // A client that cuts at the announced length would get a truncated card — invalid, rejected,
        // and with nothing to say why.
        Assert.Equal(Encoding.UTF8.GetByteCount(card.VCardRaw).ToString(), value);
        Assert.NotEqual(card.VCardRaw.Length.ToString(), value);
    }

    [Fact]
    public void TheLastModified_IsAnHttpDateInGmt()
    {
        var card = CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n") with
        {
            UpdatedAt = new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc)
        };

        // Never ISO, which nothing reads here.
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", Resolve(card, DavXml.Dav + "getlastmodified"));
    }

    [Fact]
    public void TheLastModified_ConvertsALocalStampRatherThanStampingItGmt()
    {
        var utc = new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc);
        var card = CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n") with { UpdatedAt = utc.ToLocalTime() };

        // "R" appends "GMT" whatever the kind, so a local stamp handed through untouched would
        // announce the wall clock of this machine as if it were UTC.
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", Resolve(card, DavXml.Dav + "getlastmodified"));
    }

    [Fact]
    public void TheEtag_IsTheCardHashInQuotes()
    {
        var card = CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n") with { CardHash = "abc123" };

        Assert.Equal("\"abc123\"", Resolve(card, DavXml.Dav + "getetag"));
    }

    [Fact]
    public void TheContentType_IsVCardWithItsCharset()
    {
        Assert.Equal("text/vcard; charset=utf-8",
            Resolve(DefaultCard(), DavXml.Dav + "getcontenttype"));
    }

    [Fact]
    public void ACardIsNotACollection()
    {
        Assert.Empty(ResolveCardElement(DavXml.Dav + "resourcetype").Elements());
    }

    [Fact]
    public void TheMaxResourceSize_IsTheStoresOwnConstant()
    {
        // Not a copied literal: an announced value the store would violate, or the reverse, is paid
        // for in cards refused without the client understanding why.
        Assert.Equal(ContactStore.MaxCardBytes.ToString(),
            ResolveCollection(DavXml.CardDav + "max-resource-size"));

        // The line above alone cannot fail — a hard-coded 1048576 in the table satisfies it just as
        // well as the constant does. The specified ceiling, spelled once here, is what makes moving
        // ContactStore.MaxCardBytes without moving the announcement (or the reverse) come out red.
        Assert.Equal("1048576", ResolveCollection(DavXml.CardDav + "max-resource-size"));
    }

    [Fact]
    public void TheLastModified_ReadsAnUnspecifiedStampAsUtc()
    {
        // What EF hands back from MySQL carries no kind at all; reading it as local time would shift
        // the announced date by the server's offset.
        var card = CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n") with
        {
            UpdatedAt = new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Unspecified)
        };

        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", Resolve(card, DavXml.Dav + "getlastmodified"));
    }

    [Fact]
    public void ThePrivilegeSet_AlwaysCarriesWriteAndWriteContent()
    {
        var element = ResolveCollectionElement(DavXml.Dav + "current-user-privilege-set");

        // A set that is PRESENT and INCOMPLETE puts Thunderbird in read-only mode — worse than not
        // serving it at all, which makes it write by default.
        var privileges = element.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("write", privileges);
        Assert.Contains("write-content", privileges);
    }

    [Fact]
    public void ThePrivilegeSet_CarriesTheWholeSevenOnACardToo()
    {
        var element = ResolveCardElement(DavXml.Dav + "current-user-privilege-set");

        var privileges = element.Elements()
            .Select(e => Assert.Single(e.Elements()).Name)
            .ToList();

        Assert.Equal(
            [
                DavXml.Dav + "bind", DavXml.Dav + "read", DavXml.Dav + "read-current-user-privilege-set",
                DavXml.Dav + "unbind", DavXml.Dav + "write", DavXml.Dav + "write-content",
                DavXml.Dav + "write-properties"
            ],
            privileges.OrderBy(name => name.LocalName, StringComparer.Ordinal));
    }

    [Fact]
    public void ThePrincipal_CarriesTheTwoEmptyRfc3744Properties()
    {
        var found = ResolvePrincipal(
            [DavXml.Dav + "alternate-URI-set", DavXml.Dav + "group-membership"]);

        // RFC 3744 § 4 makes them mandatory on any principal. Omitting them lets a client conclude
        // the principal is not one.
        Assert.Equal(2, found.Found.Count);
        Assert.Empty(found.Missing);
        Assert.All(found.Found, e => Assert.Empty(e.Elements()));
    }

    [Fact]
    public void ThePrincipal_IsOneAndSaysSo()
    {
        var kinds = ResolvePrincipalElement(DavXml.Dav + "resourcetype").Elements()
            .Select(e => e.Name).ToList();

        Assert.Equal([DavXml.Dav + "principal"], kinds);
    }

    [Fact]
    public void ThePrincipal_DisplaysTheAddress()
    {
        Assert.Equal(PrincipalAddress, ResolvePrincipalElement(DavXml.Dav + "displayname").Value);
    }

    [Fact]
    public void ThePrincipal_CarriesSupportedReportSet_WithExpandProperty()
    {
        var reports = ReportsOf(ResolvePrincipalElement(DavXml.Dav + "supported-report-set"));

        // RFC 6352 § 8 asks for it on the principal too, not only on the collection.
        Assert.Equal([DavXml.Dav + "expand-property"], reports);
    }

    [Fact]
    public void TheCollection_IsBothACollectionAndAnAddressbook()
    {
        var element = ResolveCollectionElement(DavXml.Dav + "resourcetype");

        var kinds = element.Elements().Select(e => e.Name).ToList();
        Assert.Contains(DavXml.Dav + "collection", kinds);
        Assert.Contains(DavXml.CardDav + "addressbook", kinds);
    }

    [Fact]
    public void SupportedAddressData_AnnouncesBothVersions()
    {
        var element = ResolveCollectionElement(DavXml.CardDav + "supported-address-data");

        // The book stores both verbatim and serves what it holds: announcing 3.0 alone would make
        // half the answers a lie.
        var versions = element.Elements().Select(e => e.Attribute("version")!.Value).ToList();
        Assert.Equal(["3.0", "4.0"], versions.Order());
    }

    [Fact]
    public void SupportedCollationSet_CarriesTheTwoRfc6352MakesMandatory()
    {
        var element = ResolveCollectionElement(DavXml.CardDav + "supported-collation-set");

        Assert.Equal(["i;ascii-casemap", "i;unicode-casemap"],
            element.Elements().Select(e => e.Value).Order());
    }

    [Fact]
    public void TheCollection_AnnouncesTheFourReportsOfTheSlice()
    {
        var reports = ReportsOf(ResolveCollectionElement(DavXml.Dav + "supported-report-set"));

        Assert.Equal(
            [
                DavXml.CardDav + "addressbook-multiget", DavXml.CardDav + "addressbook-query",
                DavXml.Dav + "expand-property", DavXml.Dav + "sync-collection"
            ],
            reports.OrderBy(name => name.LocalName, StringComparer.Ordinal));
    }

    [Fact]
    public void ACardCarriesSupportedReportSet_WithMultigetAndQuery()
    {
        var element = ResolveCardElement(DavXml.Dav + "supported-report-set");

        // RFC 6352 § 8 requires it on address resources as much as on collections.
        var reports = element.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("addressbook-multiget", reports);
        Assert.Contains("addressbook-query", reports);
    }

    [Theory]
    [InlineData("supported-privilege-set")]
    [InlineData("acl")]
    [InlineData("resource-id")]
    public void APropertyWeDoNotCarry_ComesBackAsMissingRatherThanOmitted(string localName)
    {
        var resolved = ResolveCollectionRequest([DavXml.Dav + localName]);

        // Pure omission is what makes a client wait for ever for a value it believes is on its way.
        Assert.Empty(resolved.Found);
        Assert.Single(resolved.Missing);
    }

    [Fact]
    public void TheOtherPropertiesClientsReallyAskFor_AreAlso404AndNotOmitted()
    {
        XName[] asked =
        [
            DavXml.CalendarServer + "email-address-set", DavXml.CardDav + "directory-gateway",
            DavXml.CardDav + "addressbook-description",
            XNamespace.Get("https://bitfire.at/webdav-push") + "push-transports"
        ];

        var resolved = ResolveCollectionRequest(asked);

        Assert.Empty(resolved.Found);
        Assert.Equal(asked, resolved.Missing);
    }

    [Fact]
    public void APropertyOfAnotherResourceKind_IsMissingHere()
    {
        // getctag belongs to the book; asking a card for it must answer 404, not the book's value.
        var resolved = DavProperties.Resolve(
            Named(DavXml.CalendarServer + "getctag"), CardContext(DefaultCard()));

        Assert.Empty(resolved.Found);
        Assert.Single(resolved.Missing);
    }

    [Fact]
    public void AnEmptyBook_HasABareZeroForItsCtag()
    {
        // A book that has never emitted anything has nothing to protect, and the first real ctag
        // will always differ from it. Pinned because nobody would otherwise know whether null
        // should render "0" or throw.
        Assert.Equal("0", DavSyncToken.Ctag(null));
    }

    [Fact]
    public void AnEmptyBooksToken_IsStillAUri()
    {
        // Plan c parses this one, so it may not degrade into a bare number; the empty epoch is one
        // no live book ever holds, so a client handing it back is resynchronised rather than
        // believed.
        Assert.Equal($"http://weesky.net/ns/sync/{Guid.Empty}/0", DavSyncToken.Token(null));
    }

    [Fact]
    public void TheCtagAndTheToken_BothCarryTheEpoch()
    {
        var epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var state = new SyncState(epoch, 42, 0);

        // A bare sequence would leave a hole through the fallback path: after a restore, a sleeping
        // client returning once the sequence has grown back to its remembered value would see an
        // EQUAL ctag on a divergent book, and skip resynchronising.
        Assert.Equal($"{epoch}:42", DavSyncToken.Ctag(state));
        Assert.Equal($"http://weesky.net/ns/sync/{epoch}/42", DavSyncToken.Token(state));
    }

    [Fact]
    public void TheBooksCtagAndTokenComeFromTheState_NotFromAConstant()
    {
        Assert.Equal(DavSyncToken.Ctag(State), ResolveCollection(DavXml.CalendarServer + "getctag"));
        Assert.Equal(DavSyncToken.Token(State), ResolveCollection(DavXml.Dav + "sync-token"));
    }

    [Fact]
    public void EveryHrefComesFromDavPaths()
    {
        Assert.Equal(DavPaths.Principal(UserId),
            HrefIn(ResolveCollectionElement(DavXml.Dav + "current-user-principal")));
        Assert.Equal(DavPaths.Principal(UserId),
            HrefIn(ResolveCollectionElement(DavXml.Dav + "owner")));
        Assert.Equal(DavPaths.Home(UserId),
            HrefIn(ResolvePrincipalElement(DavXml.CardDav + "addressbook-home-set")));
        Assert.Equal(DavPaths.Principal(UserId),
            HrefIn(ResolvePrincipalElement(DavXml.Dav + "principal-URL")));
    }

    [Fact]
    public void TheServiceRoot_PointsAtThePrincipalAndClaimsNoResourceType()
    {
        var root = new DavResourceContext(DavResourceKind.ServiceRoot, UserId, PrincipalAddress, null, null);

        Assert.Equal(DavPaths.Principal(UserId),
            HrefIn(Single(DavProperties.Resolve(Named(DavXml.Dav + "current-user-principal"), root))));
        Assert.Empty(Single(DavProperties.Resolve(Named(DavXml.Dav + "resourcetype"), root)).Elements());
    }

    [Fact]
    public void TheHome_IsACollectionTheUserCanRead()
    {
        var home = new DavResourceContext(DavResourceKind.Home, UserId, PrincipalAddress, null, null);

        Assert.Equal([DavXml.Dav + "collection"],
            Single(DavProperties.Resolve(Named(DavXml.Dav + "resourcetype"), home)).Elements()
                .Select(e => e.Name));
        Assert.Equal("Address Books",
            Single(DavProperties.Resolve(Named(DavXml.Dav + "displayname"), home)).Value);
    }

    [Fact]
    public void TheBook_IsNamedForTheUsersEye()
    {
        Assert.Equal("Contacts", ResolveCollection(DavXml.Dav + "displayname"));
    }

    [Fact]
    public void AllProp_LeavesOutSyncTokenAndThePrivilegeSet()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.AllProp, []), CollectionContext());

        // Both cost, and a client that wants them names them. Everything else of the closed set is
        // poured in even though its own RFC marks it "SHOULD NOT in allprop" — a stable set makes
        // approximate clients predictable, and that divergence is deliberate.
        var names = resolved.Found.Select(e => e.Name).ToList();
        Assert.DoesNotContain(DavXml.Dav + "sync-token", names);
        Assert.DoesNotContain(DavXml.Dav + "current-user-privilege-set", names);
        Assert.Contains(DavXml.CardDav + "supported-address-data", names);
        Assert.Contains(DavXml.CardDav + "max-resource-size", names);
    }

    [Fact]
    public void AllProp_NeverReportsAnythingMissing()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.AllProp, []), CollectionContext());

        Assert.Empty(resolved.Missing);
    }

    [Fact]
    public void AllProp_PoursTheTwoItLeftOutWhenAnIncludeNamesThem()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.AllProp,
                [DavXml.Dav + "sync-token", DavXml.Dav + "current-user-privilege-set"]),
            CollectionContext());

        var names = resolved.Found.Select(e => e.Name).ToList();
        Assert.Contains(DavXml.Dav + "sync-token", names);
        Assert.Contains(DavXml.Dav + "current-user-privilege-set", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void PropName_AnswersTheNamesWithoutTheValues()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.PropName, []), CollectionContext());

        Assert.NotEmpty(resolved.Found);
        Assert.All(resolved.Found, e =>
        {
            Assert.Empty(e.Elements());
            Assert.Equal(string.Empty, e.Value);
        });
    }

    [Fact]
    public void PropName_NamesTheWholeSet_IncludingWhatAllPropLeavesOut()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.PropName, []), CollectionContext());

        var names = resolved.Found.Select(e => e.Name).ToList();
        Assert.Contains(DavXml.Dav + "sync-token", names);
        Assert.Contains(DavXml.Dav + "current-user-privilege-set", names);
        Assert.Empty(resolved.Missing);
    }

    [Fact]
    public void AnEmptyBody_MeansAllProp()
    {
        Assert.Equal(DavPropertyMode.AllProp, DavPropertyRequest.Parse(null).Mode);
    }

    [Fact]
    public void TheParser_ReadsTheNamespaceAndTheLocalName_NotThePrefix()
    {
        // Bound to "a:", which no RFC example ever writes; a reader comparing "D:prop" reads nothing
        // here and answers allprop to a request that named two properties.
        var body = XDocument.Parse(
            """<a:propfind xmlns:a="DAV:"><a:prop><a:displayname/><a:getetag/></a:prop></a:propfind>""");

        var request = DavPropertyRequest.Parse(body);

        Assert.Equal(DavPropertyMode.Named, request.Mode);
        Assert.Equal([DavXml.Dav + "displayname", DavXml.Dav + "getetag"], request.Names);
    }

    [Fact]
    public void TheParser_IgnoresAnElementSpelledLikeOursInAnotherNamespace()
    {
        var body = XDocument.Parse(
            """<D:propfind xmlns:D="http://example.invalid/"><D:prop><D:displayname/></D:prop></D:propfind>""");

        var request = DavPropertyRequest.Parse(body);

        Assert.Equal(DavPropertyMode.AllProp, request.Mode);
        Assert.Empty(request.Names);
    }

    [Fact]
    public void TheParser_ReadsAllPropAndPropName()
    {
        Assert.Equal(DavPropertyMode.AllProp,
            DavPropertyRequest.Parse(XDocument.Parse(
                """<propfind xmlns="DAV:"><allprop/></propfind>""")).Mode);
        Assert.Equal(DavPropertyMode.PropName,
            DavPropertyRequest.Parse(XDocument.Parse(
                """<propfind xmlns="DAV:"><propname/></propfind>""")).Mode);
    }

    [Fact]
    public void TheParser_KeepsTheIncludeOfAnAllProp()
    {
        var body = XDocument.Parse(
            """<propfind xmlns="DAV:"><allprop/><include><sync-token/></include></propfind>""");

        var request = DavPropertyRequest.Parse(body);

        Assert.Equal(DavPropertyMode.AllProp, request.Mode);
        Assert.Equal([DavXml.Dav + "sync-token"], request.Names);
    }

    [Fact]
    public void TheParser_ReadsAPropNestedInAReportBody()
    {
        var body = XDocument.Parse(
            """
            <C:addressbook-multiget xmlns:C="urn:ietf:params:xml:ns:carddav" xmlns:D="DAV:">
              <D:prop><D:getetag/></D:prop>
              <D:href>/dav/addressbooks/x/default/a.vcf</D:href>
            </C:addressbook-multiget>
            """);

        var request = DavPropertyRequest.Parse(body);

        Assert.Equal(DavPropertyMode.Named, request.Mode);
        Assert.Equal([DavXml.Dav + "getetag"], request.Names);
    }

    private static DavCard CardWith(string raw) => new(
        ContactId, DavName, Uid, raw, "9f2c0e7a",
        new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), 41);

    private static DavCard DefaultCard() => CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n");

    private static DavPropertyRequest Named(params XName[] names) =>
        new(DavPropertyMode.Named, names);

    private static DavResourceContext CardContext(DavCard card) =>
        new(DavResourceKind.Card, UserId, PrincipalAddress, card, State);

    private static DavResourceContext CollectionContext() =>
        new(DavResourceKind.Collection, UserId, PrincipalAddress, null, State);

    private static DavResourceContext PrincipalContext() =>
        new(DavResourceKind.Principal, UserId, PrincipalAddress, null, null);

    private static XElement Single((List<XElement> Found, List<XName> Missing) resolved)
    {
        Assert.Empty(resolved.Missing);
        return Assert.Single(resolved.Found);
    }

    private static string Resolve(DavCard card, XName name) =>
        Single(DavProperties.Resolve(Named(name), CardContext(card))).Value;

    private static XElement ResolveCardElement(XName name) =>
        Single(DavProperties.Resolve(Named(name), CardContext(DefaultCard())));

    private static string ResolveCollection(XName name) => ResolveCollectionElement(name).Value;

    private static XElement ResolveCollectionElement(XName name) =>
        Single(DavProperties.Resolve(Named(name), CollectionContext()));

    private static (List<XElement> Found, List<XName> Missing) ResolveCollectionRequest(XName[] names) =>
        DavProperties.Resolve(new DavPropertyRequest(DavPropertyMode.Named, names), CollectionContext());

    private static (List<XElement> Found, List<XName> Missing) ResolvePrincipal(XName[] names) =>
        DavProperties.Resolve(new DavPropertyRequest(DavPropertyMode.Named, names), PrincipalContext());

    private static XElement ResolvePrincipalElement(XName name) =>
        Single(DavProperties.Resolve(Named(name), PrincipalContext()));

    private static string HrefIn(XElement element) =>
        Assert.Single(element.Elements(DavXml.Href)).Value;

    private static List<XName> ReportsOf(XElement supportedReportSet) =>
        [.. supportedReportSet.Elements(DavXml.Dav + "supported-report")
            .Select(report => Assert.Single(report.Elements(DavXml.Dav + "report")))
            .Select(report => Assert.Single(report.Elements()).Name)];
}
