using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Bimi;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class BimiLogoServiceTests
{
    private const string Domain = "paypal.com";
    private const string Selector = "default";
    private const string Record = "v=BIMI1; l=https://static.paypal.com/logo.svg";
    private static readonly byte[] Svg =
        Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10"/></svg>""");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 9, 9];
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero) };
    private readonly FakeDns _dns = new();
    private readonly FakeRasterizer _rasterizer = new(Png);

    private BimiLogoService Create(HttpMessageHandler handler, FakeRasterizer? rasterizer = null) =>
        new(_dns, new StubHttpClientFactory(handler), rasterizer ?? _rasterizer, _clock, NullLogger<BimiLogoService>.Instance);

    private static Func<HttpResponseMessage> Logo(byte[]? bytes = null, string type = "image/svg+xml") => () =>
        Answer(new ByteArrayContent(bytes ?? Svg), type);

    private static HttpResponseMessage Answer(HttpContent content, string type)
    {
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    private static Func<HttpResponseMessage> Redirect(string? location, HttpStatusCode status = HttpStatusCode.Found) => () =>
    {
        var response = new HttpResponseMessage(status);
        if (location is not null) response.Headers.TryAddWithoutValidation("Location", location);
        return response;
    };

    private async Task AssertCachedMissAsync(BimiLogoService service, StubHttpMessageHandler handler, int calls)
    {
        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Equal(calls, handler.Calls);
        _clock.Now += GravatarService.FailureLifetime;
        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Equal(calls, handler.Calls);
        Assert.Empty(_rasterizer.Sizes);
    }

    [Fact]
    public async Task Get_ReturnsTheRasterisedLogoAsPng_AskingTheRecordsAddress()
    {
        _dns.Records[$"{Selector}._bimi.{Domain}"] = [Record];
        var handler = new StubHttpMessageHandler(Logo());

        var image = await Create(handler).GetAsync(Domain, Selector, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(Png, image.Bytes);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(new Uri("https://static.paypal.com/logo.svg"), Assert.Single(handler.Uris));
        Assert.Equal(BimiLogoService.RasterSize, _rasterizer.Sizes.Single());
        Assert.Equal([$"{Selector}._bimi.{Domain}"], _dns.Asked);
    }

    [Fact]
    public async Task Get_UsesTheGivenSelector()
    {
        _dns.Records["brand._bimi.paypal.com"] = [Record];

        Assert.NotNull(await Create(new StubHttpMessageHandler(Logo())).GetAsync(Domain, "brand", CancellationToken.None));
        Assert.Equal(["brand._bimi.paypal.com"], _dns.Asked);
    }

    [Fact]
    public async Task Get_FallsBackOnceToTheParentDomain()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];

        var image = await Create(new StubHttpMessageHandler(Logo())).GetAsync("mail.paypal.com", Selector, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(["default._bimi.mail.paypal.com", "default._bimi.paypal.com"], _dns.Asked);
    }

    [Fact]
    public async Task Get_NeverFallsBackBeyondOneParentNorToABareTld()
    {
        _dns.Records["default._bimi.example.com"] = [Record];

        Assert.Null(await Create(new StubHttpMessageHandler()).GetAsync("a.b.example.com", Selector, CancellationToken.None));
        Assert.Equal(["default._bimi.a.b.example.com", "default._bimi.b.example.com"], _dns.Asked);

        _dns.Asked.Clear();
        Assert.NotNull(await Create(new StubHttpMessageHandler(Logo())).GetAsync("example.com", Selector, CancellationToken.None));
        Assert.Equal(["default._bimi.example.com"], _dns.Asked);

        _dns.Asked.Clear();
        Assert.Null(await Create(new StubHttpMessageHandler()).GetAsync("nothing.org", Selector, CancellationToken.None));
        Assert.Equal(["default._bimi.nothing.org"], _dns.Asked);
    }

    [Fact]
    public async Task Get_DoesNotFallBackWhenTheDomainPublishesARecordWithoutALogo()
    {
        _dns.Records["default._bimi.mail.paypal.com"] = ["v=BIMI1; l=;"];
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler();

        Assert.Null(await Create(handler).GetAsync("mail.paypal.com", Selector, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
        Assert.Single(_dns.Asked);
    }

    [Theory]
    [InlineData("no-record")]
    [InlineData("unrelated-txt")]
    [InlineData("http-logo")]
    [InlineData("two-logos")]
    public async Task Get_ReturnsNullWithoutFetching_WhenTheRecordIsMissingOrInvalid(string kind)
    {
        _dns.Records["default._bimi.paypal.com"] = kind switch
        {
            "no-record" => [],
            "unrelated-txt" => ["v=spf1 -all"],
            "http-logo" => ["v=BIMI1; l=http://static.paypal.com/logo.svg"],
            _ => ["v=BIMI1; l=https://a.paypal.com/1.svg; l=https://a.paypal.com/2.svg"]
        };
        var handler = new StubHttpMessageHandler(Logo());

        Assert.Null(await Create(handler).GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("text/html")]
    public async Task Get_RefusesALogoNotServedAsSvg(string type)
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];

        Assert.Null(await Create(new StubHttpMessageHandler(Logo(type: type))).GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Empty(_rasterizer.Sizes);
    }

    [Fact]
    public async Task Get_RefusesABodyOverThirtyTwoKilobytes()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var big = new byte[BimiLogoService.MaxBytes + 1];

        Assert.Null(await Create(new StubHttpMessageHandler(Logo(big))).GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Empty(_rasterizer.Sizes);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Get_ReturnsNullForAnErrorStatus(HttpStatusCode status)
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];

        Assert.Null(await Create(new StubHttpMessageHandler(Status(status))).GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Fact]
    public async Task Get_FollowsARelativeRedirectThroughTheBimiClient()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler(Redirect("/brand/logo.svg"), Logo());
        var factory = new StubHttpClientFactory(handler);
        var service = new BimiLogoService(_dns, factory, _rasterizer, _clock, NullLogger<BimiLogoService>.Instance);

        var image = await service.GetAsync(Domain, Selector, CancellationToken.None);

        Assert.Equal(Png, image?.Bytes);
        Assert.Equal([new Uri("https://static.paypal.com/logo.svg"), new Uri("https://static.paypal.com/brand/logo.svg")], handler.Uris);
        Assert.Equal([BimiLogoService.ClientName], factory.Names);
    }

    [Fact]
    public async Task Get_FollowsTwoRedirects()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler(
            Redirect("https://cdn.paypal.net/a.svg", HttpStatusCode.MovedPermanently),
            Redirect("https://images.paypal.org/b.svg", HttpStatusCode.TemporaryRedirect),
            Logo());

        var image = await Create(handler).GetAsync(Domain, Selector, CancellationToken.None);

        Assert.Equal(Png, image?.Bytes);
        Assert.Equal(new Uri("https://images.paypal.org/b.svg"), handler.Uris[^1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Get_FollowsEachRedirectStatus(HttpStatusCode status)
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];

        Assert.NotNull(await Create(new StubHttpMessageHandler(Redirect("/b.svg", status), Logo())).GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Fact]
    public async Task Get_MissesOnAThirdRedirect_AfterThreeRequests()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler(Redirect("/a.svg"), Redirect("/b.svg"), Redirect("/c.svg"), Logo());

        await AssertCachedMissAsync(Create(handler), handler, 3);
    }

    [Fact]
    public async Task Get_MissesOnARedirectLoop_AfterThreeRequests()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var self = Redirect("https://static.paypal.com/logo.svg");
        var handler = new StubHttpMessageHandler(self, self, self, Logo());

        await AssertCachedMissAsync(Create(handler), handler, 3);
        Assert.All(handler.Uris, uri => Assert.Equal(new Uri("https://static.paypal.com/logo.svg"), uri));
    }

    [Theory]
    [InlineData("http://static.paypal.com/logo.svg")]
    [InlineData("https://static.paypal.com:8443/logo.svg")]
    [InlineData("https://10.0.0.1/logo.svg")]
    [InlineData("https://[::1]/logo.svg")]
    [InlineData("https://user:pw@static.paypal.com/logo.svg")]
    [InlineData("ftp://static.paypal.com/logo.svg")]
    [InlineData("https://")]
    [InlineData(null)]
    public async Task Get_MissesWithoutFollowing_ARedirectToAnUnfetchableTarget(string? location)
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler(Redirect(location), Logo());

        await AssertCachedMissAsync(Create(handler), handler, 1);
    }

    [Fact]
    public async Task Get_CutsASlowSecondHopWithTheSameBudget_AsAFiveMinuteFailure()
    {
        _clock.HoldTimers = true;
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = Create(new LambdaHttpMessageHandler(async (_, ct) =>
        {
            if (++calls == 1) return Redirect("/slow.svg")();
            if (calls > 2) return Logo()();
            reached.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }));

        var pending = service.GetAsync(Domain, Selector, CancellationToken.None);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _clock.Now += BimiLogoService.FetchTimeout;

        Assert.Null(await pending);
        Assert.Equal(2, calls);
        _clock.Now += GravatarService.FailureLifetime;
        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsNullForAnUnsafeSvg()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var unsafeSvg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");

        Assert.Null(await Create(new StubHttpMessageHandler(Logo(unsafeSvg))).GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Empty(_rasterizer.Sizes);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenTheRasteriserCannotDrawIt()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];

        Assert.Null(await Create(new StubHttpMessageHandler(Logo()), new FakeRasterizer(null)).GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Fact]
    public async Task Get_TreatsARenderTimeoutAsAFiveMinuteFailureNotADayLongMiss()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var service = Create(new StubHttpMessageHandler(Logo(), Logo()), new FakeRasterizer(Png, timeouts: 1));

        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));
        _clock.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Fact]
    public async Task Get_CachesAHitForSevenDays()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler(Logo(), Logo());
        var service = Create(handler);

        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
        _clock.Now += TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Equal(1, handler.Calls);

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Get_CachesAMissForTwentyFourHours()
    {
        var service = Create(new StubHttpMessageHandler(Logo()));

        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));
        _dns.Records["default._bimi.paypal.com"] = [Record];
        _clock.Now += TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Theory]
    [InlineData("dns")]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("server-error")]
    public async Task Get_ReturnsNullOnAFailureAndAsksAgainAfterFiveMinutes(string failure)
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        _dns.Fails = failure == "dns";
        var calls = 0;
        var handler = new LambdaHttpMessageHandler((_, _) => ++calls > 1 || failure == "dns"
            ? Task.FromResult(Logo()())
            : failure switch
            {
                "http" => throw new HttpRequestException("refused"),
                "timeout" => throw new TaskCanceledException("timeout"),
                _ => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)())
            });
        var service = Create(handler);

        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));
        _dns.Fails = false;
        _clock.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Domain, Selector, CancellationToken.None));
    }

    [Fact]
    public async Task Get_GivesUpOnABodyThatStallsPastTheBudget()
    {
        _clock.HoldTimers = true;
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var handler = new StubHttpMessageHandler(() => Answer(
            new StreamContent(new ScriptedStream(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; })),
            "image/svg+xml"));
        var service = Create(handler);

        var pending = service.GetAsync(Domain, Selector, CancellationToken.None);
        await _clock.WaitForPendingTimerAsync();
        _clock.Now += BimiLogoService.FetchTimeout;

        Assert.Null(await pending);
        Assert.Null(await service.GetAsync(Domain, Selector, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Get_ConcurrentCallsForOneDomain_MakeOneFetch()
    {
        _dns.Records["default._bimi.paypal.com"] = [Record];
        var release = new TaskCompletionSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new StrongBox<int>();
        var service = Create(new LambdaHttpMessageHandler(async (_, _) =>
        {
            Interlocked.Increment(ref calls.Value);
            reached.TrySetResult();
            await release.Task;
            return Logo()();
        }));

        var callers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => service.GetAsync(Domain, Selector, CancellationToken.None)))
            .ToArray();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var images = await Task.WhenAll(callers);

        Assert.Equal(1, calls.Value);
        Assert.All(images, image => Assert.Same(images[0], image));
    }

    private sealed class FakeDns : IDnsTxtResolver
    {
        public Dictionary<string, string[]> Records { get; } = [];
        public List<string> Asked { get; } = [];
        public bool Fails { get; set; }

        public Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken cancellationToken)
        {
            Asked.Add(name);
            if (Fails) throw new InvalidOperationException("resolver down");
            return Task.FromResult<IReadOnlyList<string>>(Records.GetValueOrDefault(name, []));
        }
    }

    private sealed class FakeRasterizer(byte[]? png, int timeouts = 0) : ISvgRasterizer
    {
        public List<int> Sizes { get; } = [];

        public Task<byte[]?> ToPngAsync(byte[] svg, int size, CancellationToken cancellationToken)
        {
            Sizes.Add(size);
            return Sizes.Count <= timeouts ? throw new TimeoutException("render budget") : Task.FromResult(png);
        }
    }
}
