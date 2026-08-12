using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Opens ManageSieve (RFC 5804) sessions over TCP+STARTTLS. The SASL PLAIN identities come from
/// the <see cref="SieveConnection"/>; everything else — timeouts, TLS policy — from
/// <see cref="SieveOptions"/>, which is a client policy rather than a per-target setting.
///
/// The handshake and the session that follows it read through the same
/// <see cref="ManageSieveWire"/>, so nothing this class buffers past a line boundary is lost to
/// the verbs behind it.
/// </summary>
internal sealed class ManageSieveClient(
    IOptions<SieveOptions> options, ILogger<ManageSieveClient> logger) : IManageSieveClient
{
    private SieveOptions Options => options.Value;

    public async Task<Result<IManageSieveSession>> OpenSessionAsync(SieveConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            logger.LogError("ManageSieve target is incomplete: {Connection}", connection);
            return Result.Failure<IManageSieveSession>(SieveErrors.NotConfigured);
        }

        TcpClient? tcp = null;
        ManageSieveWire? wire = null;
        try
        {
            tcp = new TcpClient
            {
                ReceiveTimeout = Options.TimeoutSeconds * 1000,
                SendTimeout = Options.TimeoutSeconds * 1000
            };

            // Those two socket timeouts bind synchronous calls only, and every step below is async:
            // this token is what stops a server that accepts and then goes silent from holding the
            // socket — and the request behind it — for good.
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(Options.TimeoutSeconds));
            var handshakeToken = handshakeCts.Token;

            await tcp.ConnectAsync(connection.Host, connection.Port, handshakeToken);

            wire = new ManageSieveWire(tcp.GetStream(), tcp);
            tcp = null; // the wire owns the socket from here, including on the TLS failure path

            var (capabilities, status) = await ReadCapabilitiesAsync(wire, handshakeToken);
            if (!status.IsOk)
                return FailUnreachable("greeting", connection.Host, status.Message);

            if (HasCapability(capabilities, "STARTTLS"))
            {
                await wire.WriteLineAsync("STARTTLS", handshakeToken);
                var tlsStatus = await ReadSimpleStatusAsync(wire, handshakeToken);
                if (!tlsStatus.IsOk)
                    return FailUnreachable("STARTTLS", connection.Host, tlsStatus.Message);

                var secured = await wire.TryStartTlsAsync(
                    new SslClientAuthenticationOptions { TargetHost = connection.Host },
                    (_, certificate, chain, errors) => ValidateCertificate(connection.Host, certificate, chain, errors),
                    handshakeToken);
                if (!secured)
                    return FailUnreachable("STARTTLS", connection.Host, "Server sent data before the TLS handshake");

                // Server re-sends capabilities over the encrypted channel.
                var (_, postTlsStatus) = await ReadCapabilitiesAsync(wire, handshakeToken);
                if (!postTlsStatus.IsOk)
                    return FailUnreachable("post-STARTTLS handshake", connection.Host, postTlsStatus.Message);
            }
            else if (!Options.AllowCleartext)
            {
                // The next thing on this socket is a password inside a SASL PLAIN payload. The
                // banner that advertises STARTTLS arrives unencrypted, so a missing capability is
                // indistinguishable from one an attacker stripped: refuse rather than downgrade.
                logger.LogError(
                    "ManageSieve host={Host} does not advertise STARTTLS. Refusing to send the " +
                    "credentials in the clear. Set Sieve:AllowCleartext only if the link is trusted.",
                    connection.Host);
                return Fail(SieveErrors.NotSecure);
            }

            // authzid \0 authcid \0 password: there is no master account any more, so the authzid
            // stays empty — every account authenticates with its own credentials, as the mailbox
            // itself.
            var saslPayload = $"{connection.AuthorizationIdentity}\0{connection.AuthenticationIdentity}\0{connection.Password}";
            var b64 = Convert.ToBase64String(ManageSieveWire.Utf8.GetBytes(saslPayload));
            await wire.WriteLineAsync($"AUTHENTICATE \"PLAIN\" \"{b64}\"", handshakeToken);
            var authStatus = await ReadSimpleStatusAsync(wire, handshakeToken);
            if (!authStatus.IsOk)
            {
                logger.LogWarning("ManageSieve auth failed for {Connection}: {Message}", connection, authStatus.Message);
                return Fail(SieveErrors.AuthenticationFailed);
            }

            var session = new ManageSieveSession(wire, TimeSpan.FromSeconds(Options.TimeoutSeconds));
            wire = null; // ownership transferred: disposing the session now closes the connection
            return Result.Success<IManageSieveSession>(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to open ManageSieve session for {Connection}", connection);
            return Result.Failure<IManageSieveSession>(SieveErrors.Unreachable);
        }
        finally
        {
            // Exactly one of these is non-null on a failed path; both are null once the session has
            // the wire. tcp only survives a ConnectAsync that never reached the wire.
            if (wire != null) await wire.DisposeAsync();
            tcp?.Dispose();
        }
    }

    private bool ValidateCertificate(string host, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None) return true;
        if (Options.AllowInvalidCertificate)
        {
            logger.LogWarning("Ignoring TLS certificate error for ManageSieve host={Host}: {Errors}", host, sslPolicyErrors);
            return true;
        }
        logger.LogError("TLS certificate validation failed for ManageSieve host={Host}: {Errors}", host, sslPolicyErrors);
        return false;
    }

    private static Result<IManageSieveSession> Fail(string message) => Result.Failure<IManageSieveSession>(message);

    /// <summary>
    /// A handshake step the server refused. The server's own wording goes to the log, never to the
    /// client: it can disclose service state, and the caller only needs to know the rules service
    /// is unavailable.
    /// </summary>
    private Result<IManageSieveSession> FailUnreachable(string step, string host, string? detail)
    {
        logger.LogWarning("ManageSieve {Step} failed on host={Host}: {Detail}", step, host, detail);
        return Fail(SieveErrors.Unreachable);
    }

    private static bool HasCapability(IReadOnlyList<string> capabilities, string name) =>
        capabilities.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static async Task<(IReadOnlyList<string> Capabilities, ManageSieveWire.Status Status)> ReadCapabilitiesAsync(
        ManageSieveWire wire, CancellationToken cancellationToken)
    {
        var caps = new List<string>();
        while (true)
        {
            var line = await wire.ReadLineAsync(cancellationToken);
            if (line == null) return (caps, new ManageSieveWire.Status(false, wire.ReadFailure));
            if (ManageSieveWire.TryParseStatus(line, out var status)) return (caps, status);

            // Capability line is of the form: "NAME" or "NAME" "value"
            var capName = ManageSieveWire.UnquoteFirst(line);
            if (capName != null) caps.Add(capName);
        }
    }

    private static async Task<ManageSieveWire.Status> ReadSimpleStatusAsync(ManageSieveWire wire, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await wire.ReadLineAsync(cancellationToken);
            if (line == null) return new ManageSieveWire.Status(false, wire.ReadFailure);
            if (ManageSieveWire.TryParseStatus(line, out var status)) return status;
            // Skip any continuation lines (rare for AUTHENTICATE/STARTTLS).
        }
    }
}
