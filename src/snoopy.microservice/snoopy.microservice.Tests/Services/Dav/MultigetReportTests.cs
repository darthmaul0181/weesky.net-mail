using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Services.Dav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Dav;

public sealed class MultigetReportTests
{
    private static readonly XName GetEtag = DavXml.Dav + "getetag";

    [Fact]
    public async Task AnHrefTheSourceRefuses_Answers404InsideTheMultistatus()
    {
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1 });
        var context = NewContext();

        var count = await MultigetReport.WriteAsync(context.Response, Body("/m/a", "/elsewhere/a"), "/m/",
            source, CancellationToken.None);

        // One response per href, in the body's order: the refused one is a 404 beside the found one,
        // never a global error that throws away what WAS found.
        Assert.Equal(2, count);
        var responses = ResponsesOf(context.Response);
        Assert.Equal(["/m/a", "/elsewhere/a"], responses.Select(HrefOf));
        Assert.Equal("\"a\"", responses[0].Descendants(GetEtag).Single().Value);
        Assert.Equal("HTTP/1.1 404 Not Found", responses[1].Element(DavXml.Status)!.Value);
    }

    [Fact]
    public async Task AMemberTheSourceDoesNotHold_Is404_AfterOneLookup()
    {
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1 });
        var context = NewContext();

        await MultigetReport.WriteAsync(context.Response, Body("/m/gone", "/m/a", "/m/a"), "/m/",
            source, CancellationToken.None);

        // Every name of ours in ONE query, each once; a stale name in a client's list is a 404.
        Assert.Equal([["gone", "a"]], source.FindManyCalls);
        var responses = ResponsesOf(context.Response);
        Assert.Equal(3, responses.Count);
        Assert.Equal("HTTP/1.1 404 Not Found", responses[0].Element(DavXml.Status)!.Value);
    }

    [Fact]
    public async Task ThePropertiesAreResolvedByTheSource()
    {
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1 });
        var context = NewContext();

        await MultigetReport.WriteAsync(context.Response,
            Body(["/m/a"], GetEtag, DavXml.Dav + "displayname"), "/m/", source, CancellationToken.None);

        var response = ResponsesOf(context.Response).Single();
        var propstats = response.Elements(DavXml.PropStat).ToList();
        Assert.Equal("HTTP/1.1 200 OK", propstats[0].Element(DavXml.Status)!.Value);
        Assert.Equal("\"a\"", propstats[0].Descendants(GetEtag).Single().Value);
        Assert.Equal("HTTP/1.1 404 Not Found", propstats[1].Element(DavXml.Status)!.Value);
        Assert.Single(propstats[1].Descendants(DavXml.Dav + "displayname"));
    }

    [Fact]
    public async Task PastMaxHrefs_Answers507BeforeAnyLookup()
    {
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1 });
        var context = NewContext();
        var hrefs = Enumerable.Range(0, MultigetReport.MaxHrefs + 1).Select(i => $"/m/{i}").ToArray();

        var count = await MultigetReport.WriteAsync(context.Response, Body(hrefs), "/m/", source,
            CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Empty(source.FindManyCalls);
        var response = ResponsesOf(context.Response).Single();
        Assert.Equal("/m/", HrefOf(response));
        Assert.Equal("HTTP/1.1 507 Insufficient Storage", response.Element(DavXml.Status)!.Value);
        Assert.Single(response.Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }

    [Fact]
    public async Task ASourceWhosePrepareRefuses_LeavesTheResponseUntouched()
    {
        var condition = DavXml.CardDav + "supported-address-data";
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1 })
        {
            OnPrepare = _ => throw new DavPreconditionException(condition),
        };
        var context = NewContext();

        // The refusal reaches the caller, which still owns the whole response: not a byte was
        // written, no status, no header — a 403 error document can still be the entire answer.
        var refused = await Assert.ThrowsAsync<DavPreconditionException>(() => MultigetReport.WriteAsync(
            context.Response, Body("/m/a"), "/m/", source, CancellationToken.None));

        Assert.Equal(condition, refused.Condition);
        Assert.Equal(0, ((MemoryStream)context.Response.Body).Length);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("DAV"));
        Assert.Empty(source.FindManyCalls);
    }

    [Fact]
    public async Task AMemberTheResolverRefuses_Answers403WithItsConditionInsideTheMultistatus()
    {
        var condition = DavXml.CalDav + "max-instances";
        var source = new FakeMemberSource(new Dictionary<string, ulong> { ["a"] = 1, ["b"] = 2 })
        {
            OnPrepare = body => (DavPropertyRequest.Parse(body), new RefusingResolver("a", condition)),
        };
        var context = NewContext();

        var count = await MultigetReport.WriteAsync(context.Response, Body("/m/a", "/m/b"), "/m/",
            source, CancellationToken.None);

        // The document is open by the time a member is resolved: a refusal there is the member's
        // own, named — never a global 403 that could only truncate what is on the wire, and never a
        // 500 — and the member that CAN be served still is.
        Assert.Equal(2, count);
        Assert.Equal(StatusCodes.Status207MultiStatus, context.Response.StatusCode);
        var responses = ResponsesOf(context.Response);
        Assert.Equal("HTTP/1.1 403 Forbidden", responses[0].Element(DavXml.Status)!.Value);
        Assert.Single(responses[0].Element(DavXml.Error)!.Elements(condition));
        Assert.Equal("\"b\"", responses[1].Descendants(GetEtag).Single().Value);
    }

    private static DefaultHttpContext NewContext() => new() { Response = { Body = new MemoryStream() } };

    private static XDocument Body(params string[] hrefs) => Body(hrefs, GetEtag);

    private static XDocument Body(IEnumerable<string> hrefs, params XName[] properties) => new(
        new XElement(DavXml.CardDav + "addressbook-multiget",
            new XElement(DavXml.Prop, properties.Select(name => new XElement(name))),
            hrefs.Select(href => new XElement(DavXml.Href, href))));

    private static List<XElement> ResponsesOf(HttpResponse response) =>
        [.. XDocument.Parse(Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray()))
            .Root!.Elements(DavXml.Response)];

    private static string HrefOf(XElement response) => response.Element(DavXml.Href)!.Value;

    private sealed class RefusingResolver(string refused, XName condition) : IDavMemberResolver<string>
    {
        public (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, string member) =>
            member == refused
                ? throw new DavPreconditionException(condition)
                : ([new XElement(GetEtag, $"\"{member}\"")], []);
    }
}
