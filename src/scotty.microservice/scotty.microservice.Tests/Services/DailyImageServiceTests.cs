using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using weesky.Scotty.Microservice.Tests.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class DailyImageServiceTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero) };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class LambdaHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    // A content whose length is unknown up front, the case where only counting the stream protects us.
    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    // A body the test controls read by read: a stall, or a connection dropped mid-body.
    private sealed class ScriptedStream(Func<CancellationToken, Task<int>> read) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(read(cancellationToken));
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static Func<HttpResponseMessage> JpegBody(Func<CancellationToken, Task<int>> read) => () =>
    {
        var content = new StreamContent(new ScriptedStream(read));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    };

    private DailyImageService Create(HttpMessageHandler handler) =>
        new(new Factory(handler), _clock, NullLogger<DailyImageService>.Instance);

    private static Func<HttpResponseMessage> ArchiveOk(string hash = "baeeb03654162a2eaa1439de2eee4b89", string start = "202610072200") =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, BingArchiveTests.Archive(hash: hash, start: start));

    private static Func<HttpResponseMessage> Image(byte[]? bytes = null, string type = "image/jpeg") => () =>
    {
        var content = new ByteArrayContent(bytes ?? Jpeg);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    };

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    [Fact]
    public async Task Get_FetchesTheArchiveThenTheImage()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image());

        var image = await Create(handler).GetAsync("fr-FR", CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal("baeeb03654162a2eaa1439de2eee4b89", image.Version);
        Assert.Equal(Jpeg, image.Bytes);
        Assert.Equal(BingArchive.MetadataUrl("fr-FR"), handler.Uris[0]);
        Assert.Equal(new Uri("https://www.bing.com/th?id=OHR.MayotteOctopus_FR-FR2063163267_1920x1080.jpg"), handler.Uris[1]);
    }

    [Fact]
    public async Task Get_ServesTheCacheUntilBingChangesImage()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image(), ArchiveOk(hash: "next"), Image());
        var service = Create(handler);

        await service.GetAsync("fr-FR", CancellationToken.None);
        await service.GetAsync("fr-FR", CancellationToken.None);
        Assert.Equal(2, handler.Calls);

        _clock.Now = new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero);
        var next = await service.GetAsync("fr-FR", CancellationToken.None);

        Assert.Equal(4, handler.Calls);
        Assert.Equal("next", next!.Version);
    }

    [Fact]
    public async Task Get_DoesNotRefetchAStaleArchiveOnEveryVisit()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(start: "202610042200"), Image());
        var service = Create(handler);

        await service.GetAsync("fr-FR", CancellationToken.None);
        _clock.Now = _clock.Now.AddMinutes(30);
        await service.GetAsync("fr-FR", CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Get_RefusesAnArchiveThatIsNotAPlainOk(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(Status(status));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Get_RefusesAnImageRedirect()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Status(HttpStatusCode.Found));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefusesAnImageThatIsNotAJpeg()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image(type: "text/html"));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefusesAnImageDeclaredOverTheCap()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image(new byte[BingArchive.MaxImageBytes + 1]));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefusesAnUnsizedImageThatRunsOverTheCap()
    {
        var oversized = () =>
        {
            var content = new UnsizedContent(new byte[BingArchive.MaxImageBytes + 1]);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        var handler = new StubHttpMessageHandler(ArchiveOk(), oversized);

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_KeepsYesterdaysImageAndWaitsBeforeRetrying()
    {
        var handler = new StubHttpMessageHandler(
            ArchiveOk(), Image(), Status(HttpStatusCode.ServiceUnavailable), ArchiveOk(hash: "next"), Image());
        var service = Create(handler);
        var yesterday = await service.GetAsync("fr-FR", CancellationToken.None);

        _clock.Now = new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero);
        Assert.Same(yesterday, await service.GetAsync("fr-FR", CancellationToken.None));

        _clock.Now = _clock.Now.AddMinutes(14);
        Assert.Same(yesterday, await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(3, handler.Calls);

        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.Equal("next", (await service.GetAsync("fr-FR", CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task Get_TreatsATimeoutAsAFailure()
    {
        var calls = 0;
        var handler = new LambdaHandler((_, _) =>
        {
            calls++;
            throw new TaskCanceledException("timeout");
        });
        var service = Create(handler);

        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Get_ConcurrentVisitorsShareOneDownload()
    {
        var release = new TaskCompletionSource();
        var scripted = new StubHttpMessageHandler(ArchiveOk(), Image());
        var handler = new LambdaHandler(async (request, ct) =>
        {
            await release.Task;
            return await new HttpMessageInvoker(scripted, disposeHandler: false).SendAsync(request, ct);
        });
        var service = Create(handler);

        var visits = Enumerable.Range(0, 5).Select(_ => service.GetAsync("fr-FR", CancellationToken.None)).ToArray();
        release.SetResult();
        var images = await Task.WhenAll(visits);

        Assert.Equal(2, scripted.Calls);
        Assert.All(images, image => Assert.Same(images[0], image));
    }

    [Fact]
    public async Task Get_AClientCancellationDelaysNobody()
    {
        var scripted = new StubHttpMessageHandler(ArchiveOk(), Image());
        var first = true;
        var handler = new LambdaHandler(async (request, ct) =>
        {
            if (first)
            {
                first = false;
                await Task.Delay(Timeout.Infinite, ct);
            }
            return await new HttpMessageInvoker(scripted, disposeHandler: false).SendAsync(request, ct);
        });
        var service = Create(handler);
        using var leaving = new CancellationTokenSource();

        var abandoned = service.GetAsync("fr-FR", leaving.Token);
        leaving.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        Assert.NotNull(await service.GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_KeepsMarketsApart()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(hash: "fr"), Image(), ArchiveOk(hash: "en"), Image());
        var service = Create(handler);

        Assert.Equal("fr", (await service.GetAsync("fr-FR", CancellationToken.None))!.Version);
        Assert.Equal("en", (await service.GetAsync("en-US", CancellationToken.None))!.Version);
        Assert.Equal(BingArchive.MetadataUrl("en-US"), handler.Uris[2]);
    }

    [Fact]
    public async Task Get_GivesUpOnABodyThatStallsAndWaitsBeforeRetrying()
    {
        _clock.HoldTimers = true;
        var handler = new StubHttpMessageHandler(
            ArchiveOk(), JpegBody(async ct => { await Task.Delay(Timeout.Infinite, ct); return 0; }));
        var service = Create(handler);

        var pending = service.GetAsync("fr-FR", CancellationToken.None);
        await _clock.WaitForPendingTimerAsync();
        _clock.Now += DailyImageService.FetchTimeout;

        Assert.Null(await pending);
        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Get_TreatsABodyCutShortAsAFailure()
    {
        var handler = new StubHttpMessageHandler(
            ArchiveOk(), JpegBody(_ => throw new HttpIOException(HttpRequestError.ResponseEnded)));
        var service = Create(handler);

        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }
}
