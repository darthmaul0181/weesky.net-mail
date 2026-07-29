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
/// The SASL PLAIN payload is what this fixture exists for: a loopback ManageSieve server records
/// the AUTHENTICATE line so both authentication shapes can be pinned byte for byte. Crossing them
/// makes a server either refuse the session or serve the wrong mailbox.
/// </summary>
public sealed class ManageSieveClientTests
{
    private static SieveOptions Configured() => new()
    {
        Host = "sieve.home.test",
        Port = 4190,
        AllowCleartext = true,
        TimeoutSeconds = 5
    };

    private static ManageSieveClient CreateSut(SieveOptions? options = null) =>
        new(Options.Create(options ?? Configured()), NullLogger<ManageSieveClient>.Instance);

    /// <summary>Extracts the base64 argument of <c>AUTHENTICATE "PLAIN" "..."</c> and decodes it.</summary>
    private static string DecodeSasl(string authenticateLine)
    {
        var parts = authenticateLine.Split('"');
        return Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
    }

    // ----- SASL PLAIN shapes -----

    [Fact]
    public async Task OpenSessionAsync_OnTheMasterPath_ImpersonatesTheMailbox()
    {
        using var server = new FakeSieveServer();
        var connection = new SieveConnection("127.0.0.1", server.Port, "alice@weesky.be", "master", "master-secret");

        var result = await CreateSut().OpenSessionAsync(connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await result.Value.DisposeAsync();
        Assert.Equal("alice@weesky.be\0master\0master-secret", DecodeSasl(await server.AuthenticateLine));
    }

    [Fact]
    public async Task OpenSessionAsync_OnTheOwnCredentialsPath_SendsAnEmptyAuthorizationIdentity()
    {
        using var server = new FakeSieveServer();
        var connection = new SieveConnection("127.0.0.1", server.Port, string.Empty, "bob@external.test", "bob-secret");

        var result = await CreateSut().OpenSessionAsync(connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await result.Value.DisposeAsync();
        var payload = DecodeSasl(await server.AuthenticateLine);
        Assert.Equal("\0bob@external.test\0bob-secret", payload);
        Assert.StartsWith("\0", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenSessionAsync_TalksToTheConnectionHost_NotTheConfiguredOne()
    {
        using var server = new FakeSieveServer();
        var options = Configured();
        options.Host = "unreachable.invalid";
        options.Port = 1;

        var result = await CreateSut(options).OpenSessionAsync(
            new SieveConnection("127.0.0.1", server.Port, string.Empty, "bob@external.test", "bob-secret"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task OpenSessionAsync_WhenTheServerRefusesTheCredentials_NeverRelaysItsMessage()
    {
        using var server = new FakeSieveServer(authResponse: "NO \"user bob does not exist\"\r\n");

        var result = await CreateSut().OpenSessionAsync(
            new SieveConnection("127.0.0.1", server.Port, string.Empty, "bob@external.test", "bob-secret"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.AuthenticationFailed, result.Error);
    }

    [Fact]
    public async Task OpenSessionAsync_WithAnIncompleteConnection_FailsNotConfigured()
    {
        var result = await CreateSut().OpenSessionAsync(
            new SieveConnection(string.Empty, 4190, "alice@weesky.be", "master", "master-secret"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.NotConfigured, result.Error);
    }

    /// <summary>
    /// A one-shot ManageSieve server on the loopback: greeting, then whatever the client sends as
    /// its first command line — the AUTHENTICATE the tests read back.
    /// </summary>
    private sealed class FakeSieveServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource<string> _authenticateLine =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop = new();

        public FakeSieveServer(string authResponse = "OK\r\n")
        {
            _listener.Start();
            _ = ServeAsync(authResponse);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>The raw AUTHENTICATE command line the client sent.</summary>
        public Task<string> AuthenticateLine => _authenticateLine.Task;

        private async Task ServeAsync(string authResponse)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                // No STARTTLS in the banner: the tests run with AllowCleartext so the handshake
                // stops right at the AUTHENTICATE line this fixture is here to capture.
                await WriteAsync(stream, "\"IMPLEMENTATION\" \"fake\"\r\nOK\r\n");
                _authenticateLine.TrySetResult(await ReadLineAsync(stream) ?? string.Empty);
                await WriteAsync(stream, authResponse);
                await Task.Delay(Timeout.Infinite, _stop.Token);
            }
            catch (Exception ex)
            {
                _authenticateLine.TrySetException(ex);
            }
        }

        private Task WriteAsync(Stream stream, string text) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(text), _stop.Token).AsTask();

        private async Task<string?> ReadLineAsync(Stream stream)
        {
            var buffer = new List<byte>();
            var one = new byte[1];
            while (await stream.ReadAsync(one, _stop.Token) != 0)
            {
                if (one[0] == (byte)'\n')
                {
                    if (buffer.Count > 0 && buffer[^1] == (byte)'\r') buffer.RemoveAt(buffer.Count - 1);
                    return Encoding.UTF8.GetString(buffer.ToArray());
                }
                buffer.Add(one[0]);
            }
            return buffer.Count == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
