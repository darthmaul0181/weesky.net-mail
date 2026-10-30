using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class GravatarServiceTests
{
    private const string Address = "bob@example.com";
    private const string AddressHash = "5ff860bf1190596c7188ab851db691f0f3169c453936e9e1eba2f9a47f7a0018";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero) };

    private GravatarService Create(HttpMessageHandler handler, ILogger<GravatarService>? logger = null) =>
        new(new StubHttpClientFactory(handler), _clock, logger ?? NullLogger<GravatarService>.Instance);

    private static Func<HttpResponseMessage> Image(byte[]? bytes = null, string type = "image/png") => () =>
        Answer(new ByteArrayContent(bytes ?? Png), type);

    private static HttpResponseMessage Answer(HttpContent content, string type)
    {
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    // Holds every request until released, counting how many reached the upstream and signalling the first.
    private static LambdaHttpMessageHandler Gated(TaskCompletionSource release, StrongBox<int> calls, TaskCompletionSource? reached = null) =>
        new(async (_, _) =>
        {
            Interlocked.Increment(ref calls.Value);
            reached?.TrySetResult();
            await release.Task;
            return Image()();
        });

    [Fact]
    public void HashOf_IgnoresCaseAndSurroundingSpace()
    {
        var hash = GravatarService.HashOf(" Bob@Example.COM ");

        Assert.Equal(GravatarService.HashOf(Address), hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public async Task Get_RequestsFixedHostWithSizeAndNotFoundDefault()
    {
        var handler = new StubHttpMessageHandler(Image());

        await Create(handler).GetAsync(" Bob@Example.COM ", CancellationToken.None);

        Assert.Equal(new Uri($"https://gravatar.com/avatar/{AddressHash}?s=80&d=404"), Assert.Single(handler.Uris));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public async Task Get_ReturnsBytesAndContentTypeOfAnAllowedImage(string type)
    {
        var image = await Create(new StubHttpMessageHandler(Image(type: type))).GetAsync(Address, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(Png, image.Bytes);
        Assert.Equal(type, image.ContentType);
    }

    [Fact]
    public async Task Get_ReturnsNullOn404_AndDoesNotAskAgainWithin24Hours_ButAsksAfter()
    {
        var handler = new StubHttpMessageHandler(Status(HttpStatusCode.NotFound), Image());
        var service = Create(handler);

        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        _clock.Now += TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(1, handler.Calls);

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Get_CachesAHitForSevenDays()
    {
        var handler = new StubHttpMessageHandler(Image(), Image([1, 2]));
        var service = Create(handler);

        var first = await service.GetAsync(Address, CancellationToken.None);
        _clock.Now += TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1);
        Assert.Same(first, await service.GetAsync(" BOB@example.com", CancellationToken.None));
        Assert.Equal(1, handler.Calls);

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal([1, 2], (await service.GetAsync(Address, CancellationToken.None))!.Bytes);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Get_TreatsAnOversizeBodyAsAMiss_AskedAgainAfter24Hours()
    {
        var oversize = Image(new byte[GravatarService.MaxBytes + 1]);
        var declared = new StubHttpMessageHandler(oversize, oversize);
        var streamed = new StubHttpMessageHandler(
            () => Answer(new UnsizedContent(new byte[GravatarService.MaxBytes + 1]), "image/png"));
        var logger = new Mock<ILogger<GravatarService>>();
        var service = Create(declared, logger.Object);

        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        _clock.Now += TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(1, declared.Calls);
        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(2, declared.Calls);
        Assert.Null(await Create(streamed).GetAsync(Address, CancellationToken.None));
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.EndsWith("body over the size cap")),
            null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Get_TreatsAnEmptyBodyAsAMiss()
    {
        var logger = new Mock<ILogger<GravatarService>>();

        Assert.Null(await Create(new StubHttpMessageHandler(Image([])), logger.Object).GetAsync(Address, CancellationToken.None));
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.EndsWith("body empty")),
            null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task Get_AcceptsABodyExactlyAtTheCap()
    {
        var atTheCap = new StubHttpMessageHandler(
            () => Answer(new UnsizedContent(new byte[GravatarService.MaxBytes]), "image/png"));

        var capped = await Create(atTheCap).GetAsync(Address, CancellationToken.None);

        Assert.Equal(GravatarService.MaxBytes, capped!.Bytes.Length);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public async Task Get_RefusesNonImageContentTypes(string type)
    {
        var handler = new StubHttpMessageHandler(Image(type: type));

        Assert.Null(await Create(handler).GetAsync(Address, CancellationToken.None));
    }

    [Fact]
    public async Task Get_DoesNotFollowARedirect()
    {
        var handler = new StubHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://elsewhere.example/a.png") }
        });

        Assert.Null(await Create(handler).GetAsync(Address, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("server-error")]
    [InlineData("rate-limited")]
    [InlineData("refused")]
    public async Task Get_OnTimeoutOrServerError_ReturnsNullAndRetriesAfterFiveMinutes(string failure)
    {
        var calls = 0;
        var handler = new LambdaHttpMessageHandler((_, _) => ++calls > 1
            ? Task.FromResult(Image()())
            : failure switch
            {
                "timeout" => throw new TaskCanceledException("timeout"),
                "refused" => throw new HttpRequestException("refused"),
                "rate-limited" => Task.FromResult(Status(HttpStatusCode.TooManyRequests)()),
                _ => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)())
            });
        var service = Create(handler);

        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        _clock.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1);
        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(1, calls);

        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.NotNull(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Get_GivesUpOnABodyThatStallsPastTheBudget()
    {
        _clock.HoldTimers = true;
        var handler = new StubHttpMessageHandler(() => Answer(
            new StreamContent(new ScriptedStream(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; })),
            "image/png"));
        var service = Create(handler);

        var pending = service.GetAsync(Address, CancellationToken.None);
        await _clock.WaitForPendingTimerAsync();
        _clock.Now += GravatarService.FetchTimeout;

        Assert.Null(await pending);
        Assert.Null(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Get_ConcurrentCallsForOneAddress_MakeOneUpstreamRequest()
    {
        var release = new TaskCompletionSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new StrongBox<int>();
        var service = Create(Gated(release, calls, reached));

        var callers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => service.GetAsync(Address, CancellationToken.None)))
            .ToArray();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var images = await Task.WhenAll(callers);

        Assert.Equal(1, calls.Value);
        Assert.All(images, image => Assert.Same(images[0], image));
    }

    [Fact]
    public async Task Get_AVisitorsCancellationIsNotAFailure()
    {
        var release = new TaskCompletionSource();
        var calls = new StrongBox<int>();
        var service = Create(Gated(release, calls));
        using var leaving = new CancellationTokenSource();

        var abandoned = service.GetAsync(Address, leaving.Token);
        var staying = service.GetAsync(Address, CancellationToken.None);
        leaving.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        release.SetResult();

        Assert.NotNull(await staying);
        Assert.NotNull(await service.GetAsync(Address, CancellationToken.None));
        Assert.Equal(1, calls.Value);
    }
}
