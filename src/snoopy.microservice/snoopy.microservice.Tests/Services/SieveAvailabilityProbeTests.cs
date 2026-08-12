using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The probe only ever reads the RFC 5804 greeting — never STARTTLS, never AUTHENTICATE — so these
/// fixtures are simpler than <see cref="ManageSieveClientTests"/>'s: a banner and a status line is
/// everything a real Dovecot ManageSieve service writes before any client speaks.
/// </summary>
public sealed class SieveAvailabilityProbeTests
{
    private static SieveOptions Options(int timeoutSeconds = 1) => new() { TimeoutSeconds = timeoutSeconds };

    private static SieveAvailabilityProbe CreateSut(SieveOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options()), NullLogger<SieveAvailabilityProbe>.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsAvailableAsync_WithABlankHost_ReturnsFalseWithoutConnecting(string host)
    {
        var available = await CreateSut().IsAvailableAsync(host, 4190, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenTheServerGreetsOk_ReturnsTrue()
    {
        using var server = new FakeGreetingServer("\"IMPLEMENTATION\" \"Dovecot Pigeonhole\"\r\n\"SASL\" \"PLAIN\"\r\nOK\r\n");

        var available = await CreateSut().IsAvailableAsync("127.0.0.1", server.Port, CancellationToken.None);

        Assert.True(available);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenTheServerGreetsBye_ReturnsFalse()
    {
        using var server = new FakeGreetingServer("BYE \"Too many connections\"\r\n");

        var available = await CreateSut().IsAvailableAsync("127.0.0.1", server.Port, CancellationToken.None);

        Assert.False(available);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenNothingListensOnThePort_ReturnsFalse()
    {
        // A loopback listener started and immediately stopped frees the port but nothing else
        // claims it in the meantime, so the connection is refused rather than timing out.
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var available = await CreateSut().IsAvailableAsync("127.0.0.1", port, CancellationToken.None);

        Assert.False(available);
    }

    /// <summary>The probe's own timeout must fire even though the server never writes anything.</summary>
    [Fact]
    public async Task IsAvailableAsync_WhenTheServerAcceptsThenGoesSilent_FailsWithinTheTimeout()
    {
        using var server = new SilentServer();

        var available = await CreateSut(Options(timeoutSeconds: 1))
            .IsAvailableAsync("127.0.0.1", server.Port, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(available);
    }

    [Fact]
    public async Task IsAvailableAsync_MemoizesPerHostAndPort_ForTheLifeOfTheInstance()
    {
        using var server = new FakeGreetingServer("OK\r\n", oneShot: true);
        var sut = CreateSut();

        var first = await sut.IsAvailableAsync("127.0.0.1", server.Port, CancellationToken.None);
        // The one-shot server closed its listener after the first accept: a second real connection
        // attempt would fail. A cached result must not attempt one.
        var second = await sut.IsAvailableAsync("127.0.0.1", server.Port, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, server.AcceptCount);
    }

    /// <summary>Accepts the connection and never writes a byte.</summary>
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public SilentServer()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task AcceptAsync()
        {
            try { await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { /* torn down */ }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>A loopback ManageSieve server that writes a fixed greeting to every connection it
    /// accepts, and nothing past the greeting — the probe never sends a byte.</summary>
    private sealed class FakeGreetingServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly string _greeting;
        private readonly bool _oneShot;
        private int _acceptCount;

        public FakeGreetingServer(string greeting, bool oneShot = false)
        {
            _greeting = greeting;
            _oneShot = oneShot;
            _listener.Start();
            _ = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int AcceptCount => _acceptCount;

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _acceptCount);
                    await using var stream = client.GetStream();
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(_greeting), _stop.Token);
                    if (_oneShot) return;
                }
            }
            catch { /* torn down */ }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
