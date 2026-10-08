using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

/// <summary>The pipeline order of Program.cs, with three stand-in endpoints for the API.</summary>
public sealed class FrontendHostingTests : IAsyncLifetime
{
    private const string Index = "<!doctype html><div id=\"root\"></div>";
    private readonly DirectoryInfo dist = Directory.CreateTempSubdirectory("scotty-dist-");
    private WebApplication? app;

    public Task InitializeAsync()
    {
        File.WriteAllText(Path.Combine(dist.FullName, "index.html"), Index);
        File.WriteAllBytes(Path.Combine(dist.FullName, "icon-192.png"), [0x89, 0x50, 0x4E, 0x47]);
        Directory.CreateDirectory(Path.Combine(dist.FullName, "assets"));
        File.WriteAllText(Path.Combine(dist.FullName, "assets", "app-abc123.js"), "console.log(1)");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        dist.Delete(recursive: true);
    }

    private FrontendSettings OneAddress => new(dist.FullName, "", null);

    private FrontendSettings TwoAddresses => new(dist.FullName, "https://api.monmail.net", "api.monmail.net");

    private async Task<HttpClient> StartAsync(FrontendSettings? frontend)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        app = builder.Build();
        app.UseSecurityHeaders(frontend);
        app.UseFrontendHosting(frontend);
        app.MapGet("/api/ping", () => "api");
        app.MapGet("/dav/ping", () => "dav");
        app.MapGet("/health", () => "Healthy");
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Fact]
    public async Task WithoutFrontend_NothingIsServedAndTheApiKeepsItsPolicy()
    {
        var client = await StartAsync(null);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        var api = await client.GetAsync("/api/ping");
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", api.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task ADeepLink_AnswersTheIndexUncached()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/mail/inbox");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Index, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task TheRoot_AnswersTheIndex()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal(Index, await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task AnAsset_IsCachedForAYear()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/assets/app-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task AMissingAsset_IsNotFound()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/absent.js")).StatusCode);
    }

    [Fact]
    public async Task AFileBesideTheIndex_IsServedUncached()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/icon-192.png");

        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/dav/unknown")]
    [InlineData("/.well-known/unknown")]
    [InlineData("/API/unknown")]
    public async Task AnApiAddress_NeverAnswersTheIndex(string path)
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("root", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheApi_KeepsItsStrictPolicyBesideThePages()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/api/ping");

        Assert.Equal("api", await response.Content.ReadAsStringAsync());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task AWriteToAPageAddress_IsNotFound()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.PostAsync("/mail/inbox", new StringContent("x"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfigJs_CarriesTheApiBaseUncached()
    {
        var client = await StartAsync(TwoAddresses);

        var response = await client.GetAsync("http://box.monmail.net/config.js");

        Assert.Equal("window.SCOTTY_CONFIG = {\"apiBase\":\"https://api.monmail.net\"};\n", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ConfigJs_IsEmptyForOneAddress()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal("window.SCOTTY_CONFIG = {\"apiBase\":\"\"};\n", await client.GetStringAsync("/config.js"));
    }

    [Fact]
    public async Task ConfigJs_CannotBeBrokenOutOfByItsValue()
    {
        var client = await StartAsync(new FrontendSettings(dist.FullName, "https://a.example\"</script><script>alert(1)//", null));

        var body = await client.GetStringAsync("/config.js");

        Assert.DoesNotContain("</script>", body);
        Assert.DoesNotContain("a.example\"", body);
    }

    [Theory]
    [InlineData("api.monmail.net", "/api/ping", HttpStatusCode.OK)]
    [InlineData("api.monmail.net", "/", HttpStatusCode.NotFound)]
    [InlineData("api.monmail.net", "/config.js", HttpStatusCode.NotFound)]
    [InlineData("box.monmail.net", "/", HttpStatusCode.OK)]
    [InlineData("box.monmail.net", "/api/ping", HttpStatusCode.NotFound)]
    [InlineData("box.monmail.net", "/dav/ping", HttpStatusCode.NotFound)]
    [InlineData("api.monmail.net", "/health", HttpStatusCode.OK)]
    [InlineData("box.monmail.net", "/health", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", "/health", HttpStatusCode.OK)]
    public async Task TwoAddresses_EachServeOnlyTheirOwnRole(string host, string path, HttpStatusCode expected)
    {
        var client = await StartAsync(TwoAddresses);

        Assert.Equal(expected, (await client.GetAsync($"http://{host}{path}")).StatusCode);
    }

    [Fact]
    public async Task OneAddress_ServesBothRolesOnAnyHost()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("http://anything.example/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("http://anything.example/")).StatusCode);
    }

    [Theory]
    [InlineData("API.MonMail.net:8443", "/", true)]
    [InlineData("API.MonMail.net:8443", "/api/ping", false)]
    [InlineData("Box.MonMail.net", "/api/ping", true)]
    [InlineData("Box.MonMail.net", "/mail/inbox", false)]
    public void TheHost_IsComparedWithoutCaseOrPort(string host, string path, bool misrouted)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;

        Assert.Equal(misrouted, FrontendHosting.Misrouted(context.Request, TwoAddresses));
    }
}
