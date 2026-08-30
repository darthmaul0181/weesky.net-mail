using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class WellKnownControllerTests : IAsyncLifetime
{
    private DavTestServer server = null!;

    public async Task InitializeAsync() => server = await DavTestServer.StartAsync();

    public Task DisposeAsync() => server.DisposeAsync().AsTask();

    [Theory]
    [InlineData("GET")]
    [InlineData("PROPFIND")]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    [InlineData("REPORT")]
    [InlineData("PUT")]
    public async Task TheWellKnown_Redirects_WhateverTheMethod(string method)
    {
        var response = await server.SendAsync(method, "/.well-known/carddav");

        // DAVx5 and Thunderbird send PROPFIND here, not GET: a redirect bound to GET hands them a
        // 405 on the very first gesture of discovery.
        Assert.Equal(301, response.StatusCode);
        Assert.Equal("/dav/", response.Header("Location"));
    }

    [Fact]
    public async Task TheWellKnown_IsAnonymous()
    {
        var response = await server.SendUnauthenticated("PROPFIND", "/.well-known/carddav");

        // A 401 on a public redirect is a gratuitous obstacle before the client even knows where to
        // authenticate.
        Assert.Equal(301, response.StatusCode);
    }

    [Fact]
    public async Task TheWellKnown_BoundsItsCaching()
    {
        var response = await server.SendAsync("GET", "/.well-known/carddav");

        // A bare 301 caches for ever: changing the /dav path one day would become impossible on
        // devices already paired.
        var cacheControl = response.Header("Cache-Control");
        Assert.NotNull(cacheControl);
        Assert.Contains("max-age", cacheControl);
    }
}
