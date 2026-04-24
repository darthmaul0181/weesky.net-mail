using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Text;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services
{
    public class DovecotQuotaClientTests
    {
        private static readonly DovecotOptions ValidOptions = new()
        {
            ApiUrl = "http://fake-dovecot/doveadm/v1",
            ApiKey = "test-api-key"
        };

        private static DovecotQuotaClient CreateSut(HttpResponseMessage? response = null, DovecotOptions? options = null)
        {
            var handler = response != null
                ? new FakeHttpMessageHandler(response)
                : new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var http = new HttpClient(handler);
            return new DovecotQuotaClient(http, Options.Create(options ?? ValidOptions), Mock.Of<ILogger<DovecotQuotaClient>>());
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task GetQuotaAsync_WithNullUser_ThrowsArgumentNullException()
        {
            var sut = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetQuotaAsync(null!));
        }

        [Fact]
        public async Task GetQuotaAsync_WithMissingApiUrl_ReturnsFailure()
        {
            var sut = CreateSut(options: new DovecotOptions { ApiUrl = "", ApiKey = "key" });

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WithMissingApiKey_ReturnsFailure()
        {
            var sut = CreateSut(options: new DovecotOptions { ApiUrl = "http://fake/api", ApiKey = "" });

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WithHttpError_ReturnsFailure()
        {
            var sut = CreateSut(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WithValidResponse_ReturnsSuccess()
        {
            var json = """[[  "doveadmResponse",[{"type":"STORAGE","value":1024,"limit":10240},{"type":"MESSAGE","value":5,"limit":1000}],"q1"]]""";
            var sut = CreateSut(JsonResponse(json));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task GetQuotaAsync_ConvertsStorageKibibytesToBytes()
        {
            var json = """[["doveadmResponse",[{"type":"STORAGE","value":2,"limit":10}],"q1"]]""";
            var sut = CreateSut(JsonResponse(json));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.Equal(2 * 1024, result.Value.StorageBytesUsed);
            Assert.Equal(10 * 1024, result.Value.StorageBytesLimit);
        }

        [Fact]
        public async Task GetQuotaAsync_ParsesMessageCounts()
        {
            var json = """[["doveadmResponse",[{"type":"MESSAGE","value":42,"limit":500}],"q1"]]""";
            var sut = CreateSut(JsonResponse(json));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.Equal(42, result.Value.MessageCount);
            Assert.Equal(500, result.Value.MessageLimit);
        }

        [Fact]
        public async Task GetQuotaAsync_WithDovecotErrorKind_ReturnsFailure()
        {
            var json = """[["error",{"type":"NOTFOUND","exitCode":68},"q1"]]""";
            var sut = CreateSut(JsonResponse(json));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WithUnexpectedResponseKind_ReturnsFailure()
        {
            var json = """[["unknownKind",{},"q1"]]""";
            var sut = CreateSut(JsonResponse(json));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WithEmptyArray_ReturnsFailure()
        {
            var sut = CreateSut(JsonResponse("[]"));

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WithNetworkException_ReturnsFailure()
        {
            var http = new HttpClient(new ExceptionHttpMessageHandler());
            var sut = new DovecotQuotaClient(http, Options.Create(ValidOptions), Mock.Of<ILogger<DovecotQuotaClient>>());

            var result = await sut.GetQuotaAsync(new User("john@example.com"));

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task GetQuotaAsync_WhenCancelled_PropagatesCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var sut = CreateSut();

            // TaskCanceledException derives from OperationCanceledException
            var ex = await Record.ExceptionAsync(() => sut.GetQuotaAsync(new User("john@example.com"), cts.Token));
            Assert.IsAssignableFrom<OperationCanceledException>(ex);
        }

        private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(response);
            }
        }

        private sealed class ExceptionHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Simulated network failure");
        }
    }
}
