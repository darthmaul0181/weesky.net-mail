using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Opens ManageSieve (RFC 5804) sessions over TCP+STARTTLS, authenticating with
/// the configured master credentials (SASL PLAIN with the target user as
/// authorization identity).
/// </summary>
internal sealed class ManageSieveClient : IManageSieveClient
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly byte[] CrLf = { 0x0D, 0x0A };

    private readonly SieveOptions _options;
    private readonly ILogger<ManageSieveClient> _logger;

    public ManageSieveClient(IOptions<SieveOptions> options, ILogger<ManageSieveClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IManageSieveSession>> OpenSessionAsync(string targetUser, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUser))
            return Result.Failure<IManageSieveSession>("Target user is required");

        if (string.IsNullOrWhiteSpace(_options.Host) ||
            string.IsNullOrWhiteSpace(_options.MasterUser) ||
            string.IsNullOrWhiteSpace(_options.MasterPassword))
        {
            _logger.LogError("ManageSieve is not configured (Host/MasterUser/MasterPassword missing)");
            return Result.Failure<IManageSieveSession>("Rules service is not configured");
        }

        TcpClient? tcp = null;
        Stream? stream = null;
        try
        {
            tcp = new TcpClient
            {
                ReceiveTimeout = _options.TimeoutSeconds * 1000,
                SendTimeout = _options.TimeoutSeconds * 1000
            };

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                await tcp.ConnectAsync(_options.Host, _options.Port, connectCts.Token);
            }

            stream = tcp.GetStream();

            var (capabilities, status) = await ReadCapabilitiesAsync(stream, cancellationToken);
            if (!status.IsOk)
                return Fail($"Greeting failed: {status.Message}");

            if (HasCapability(capabilities, "STARTTLS"))
            {
                await WriteLineAsync(stream, "STARTTLS", cancellationToken);
                var tlsStatus = await ReadSimpleStatusAsync(stream, cancellationToken);
                if (!tlsStatus.IsOk)
                    return Fail($"STARTTLS rejected: {tlsStatus.Message}");

                var ssl = new SslStream(stream, leaveInnerStreamOpen: false, CertificateValidationCallback);
                await ssl.AuthenticateAsClientAsync(_options.Host);
                stream = ssl;

                // Server re-sends capabilities over the encrypted channel.
                var (_, postTlsStatus) = await ReadCapabilitiesAsync(stream, cancellationToken);
                if (!postTlsStatus.IsOk)
                    return Fail($"Post-STARTTLS handshake failed: {postTlsStatus.Message}");
            }
            else if (!_options.AllowCleartext)
            {
                // The next thing on this socket is the master password inside a SASL PLAIN
                // payload. The banner that advertises STARTTLS arrives unencrypted, so a
                // missing capability is indistinguishable from one an attacker stripped:
                // refuse rather than downgrade silently.
                _logger.LogError(
                    "ManageSieve host={Host} does not advertise STARTTLS. Refusing to send the master " +
                    "credentials in the clear. Set Sieve:AllowCleartext only if the link is trusted.",
                    _options.Host);
                return Fail("Rules service refused: the connection could not be secured");
            }

            var saslPayload = $"{targetUser}\0{_options.MasterUser}\0{_options.MasterPassword}";
            var b64 = Convert.ToBase64String(Utf8.GetBytes(saslPayload));
            await WriteLineAsync(stream, $"AUTHENTICATE \"PLAIN\" \"{b64}\"", cancellationToken);
            var authStatus = await ReadSimpleStatusAsync(stream, cancellationToken);
            if (!authStatus.IsOk)
            {
                _logger.LogWarning("ManageSieve auth failed for target={Target}: {Message}", targetUser, authStatus.Message);
                return Fail("Authentication failed");
            }

            var capturedTcp = tcp;
            var capturedStream = stream;
            var session = new ManageSieveSession(stream, onDisposeAsync: () =>
            {
                try { capturedStream.Dispose(); } catch { /* ignore */ }
                try { capturedTcp.Dispose(); } catch { /* ignore */ }
                return ValueTask.CompletedTask;
            });
            tcp = null; // ownership transferred
            stream = null;
            return Result.Success<IManageSieveSession>(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to open ManageSieve session for target={Target}", targetUser);
            return Result.Failure<IManageSieveSession>("Unable to connect to rules service");
        }
        finally
        {
            stream?.Dispose();
            tcp?.Dispose();
        }
    }

    private bool CertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None) return true;
        if (_options.AllowInvalidCertificate)
        {
            _logger.LogWarning("Ignoring TLS certificate error for ManageSieve host={Host}: {Errors}", _options.Host, sslPolicyErrors);
            return true;
        }
        _logger.LogError("TLS certificate validation failed for ManageSieve host={Host}: {Errors}", _options.Host, sslPolicyErrors);
        return false;
    }

    private static Result<IManageSieveSession> Fail(string message) => Result.Failure<IManageSieveSession>(message);

    private static bool HasCapability(IReadOnlyList<string> capabilities, string name) =>
        capabilities.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static async Task<(IReadOnlyList<string> Capabilities, Status Status)> ReadCapabilitiesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var caps = new List<string>();
        while (true)
        {
            var line = await ReadLineAsync(stream, cancellationToken);
            if (line == null) return (caps, new Status(false, "Connection closed"));
            if (TryParseStatus(line, out var status)) return (caps, status);

            // Capability line is of the form: "NAME" or "NAME" "value"
            var capName = ExtractFirstQuoted(line);
            if (capName != null) caps.Add(capName);
        }
    }

    private static async Task<Status> ReadSimpleStatusAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadLineAsync(stream, cancellationToken);
            if (line == null) return new Status(false, "Connection closed");
            if (TryParseStatus(line, out var status)) return status;
            // Skip any continuation lines (rare for AUTHENTICATE/STARTTLS).
        }
    }

    private static bool TryParseStatus(string line, out Status status)
    {
        if (StartsWithKeyword(line, "OK"))
        {
            status = new Status(true, line.Length > 2 ? line.Substring(3) : string.Empty);
            return true;
        }
        if (StartsWithKeyword(line, "NO"))
        {
            status = new Status(false, line.Length > 2 ? line.Substring(3) : "Rejected");
            return true;
        }
        if (StartsWithKeyword(line, "BYE"))
        {
            status = new Status(false, line.Length > 3 ? line.Substring(4) : "Server closed the connection");
            return true;
        }
        status = default;
        return false;
    }

    private static bool StartsWithKeyword(string line, string keyword)
    {
        if (!line.StartsWith(keyword, StringComparison.Ordinal)) return false;
        return line.Length == keyword.Length || line[keyword.Length] == ' ';
    }

    private static string? ExtractFirstQuoted(string line)
    {
        int start = line.IndexOf('"');
        if (start < 0) return null;
        var sb = new StringBuilder();
        for (int i = start + 1; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length) { sb.Append(line[++i]); continue; }
            if (c == '"') return sb.ToString();
            sb.Append(c);
        }
        return null;
    }

    private static async Task WriteLineAsync(Stream stream, string line, CancellationToken cancellationToken)
    {
        var bytes = Utf8.GetBytes(line);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.WriteAsync(CrLf, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Unbuffered line read used only during the handshake. Once the session is
    /// authenticated and handed to <see cref="ManageSieveSession"/>, that class
    /// uses its own buffered reader.
    /// </summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, cancellationToken);
            if (read == 0)
                return ms.Length == 0 ? null : Utf8.GetString(ms.ToArray());
            if (one[0] == 0x0A)
            {
                var bytes = ms.ToArray();
                int len = bytes.Length;
                if (len > 0 && bytes[len - 1] == 0x0D) len--;
                return Utf8.GetString(bytes, 0, len);
            }
            ms.WriteByte(one[0]);
        }
    }

    private readonly record struct Status(bool IsOk, string Message);
}
