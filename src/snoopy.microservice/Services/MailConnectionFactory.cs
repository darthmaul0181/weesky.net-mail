using System.Net.Security;
using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Security;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Where and how to reach one of the mail services, read from the account's
/// <see cref="MailAccountConnection"/>. <c>Protocol</c> names it in log messages ("IMAP"), and
/// <c>ConfigurationKey</c> is the setting whose absence means "not configured", so the log line
/// points at what to fill in.
/// </summary>
internal readonly record struct MailEndpoint(
    string Protocol, string ConfigurationKey, string Host, int Port, SecureSocketOptions Security, bool IsConfigured);

/// <summary>
/// Opens one connection per request — no pooling, the Rainloop model: guard on an unconfigured
/// endpoint, a generic message to the client with the detail logged, and ownership of the client
/// transferred to the session on success so the finally block is a no-op on the happy path.
///
/// Shared by the IMAP and SMTP factories, which were the same file twice down to a byte-identical
/// certificate callback — including the rule that matters most here, that an authentication
/// failure must never echo the server's message back to the caller.
///
/// Endpoints come from the connection record; the options only supply what stays global to the
/// service (timeout, certificate policy), through IOptionsMonitor so a correction in
/// appsettings.json takes effect without restarting and dropping live sessions.
/// </summary>
internal abstract class MailConnectionFactory<TClient, TSession>(
    IOptionsMonitor<MailOptions> options, ILogger logger)
    where TClient : MailService
{
    protected ILogger Logger { get; } = logger;

    protected abstract MailEndpoint Endpoint(MailAccountConnection connection);

    protected abstract TClient CreateClient();

    /// <summary>Wraps the connected client. The session owns it from here, disposal included.</summary>
    protected abstract TSession CreateSession(TClient client);

    public async Task<Result<TSession>> OpenAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.Username))
            throw new ArgumentException("Username is required", nameof(connection));

        var endpoint = Endpoint(connection);

        if (!endpoint.IsConfigured)
        {
            Logger.LogError("{Protocol} is not configured ({ConfigurationKey} missing)",
                endpoint.Protocol, endpoint.ConfigurationKey);
            return Result.Failure<TSession>("Mail service is not configured");
        }

        // The configuration-level notice, independent of whether the server ever answers. What
        // actually crossed the wire is decided after the connect, below.
        if (endpoint.Security is SecureSocketOptions.None)
            Logger.LogWarning(
                "{Protocol} endpoint {Host}:{Port} is configured without transport security",
                endpoint.Protocol, endpoint.Host, endpoint.Port);

        TClient? client = null;

        try
        {
            client = CreateClient();
            client.ServerCertificateValidationCallback =
                (_, _, _, errors) => ValidateCertificate(endpoint.Protocol, errors);
            client.Timeout = options.CurrentValue.TimeoutSeconds * 1000;

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(options.CurrentValue.TimeoutSeconds));
                await client.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.Security, connectCts.Token);

                // Only the connected client knows whether TLS actually happened: Auto and
                // StartTlsWhenAvailable negotiate, so a server that drops STARTTLS — or an attacker
                // stripping it from the pre-auth banner — lands here with the configured value intact.
                // AuthenticateAsync sends the password whether or not the login succeeds.
                if (!client.IsSecure)
                {
                    if (!options.CurrentValue.AllowCleartext)
                    {
                        Logger.LogError(
                            "Refusing to authenticate over an unencrypted {Protocol} connection to {Host}:{Port}; " +
                            "set Mail:AllowCleartext if the link is genuinely trusted",
                            endpoint.Protocol, endpoint.Host, endpoint.Port);
                        return Result.Failure<TSession>("Unable to connect to the mail service");
                    }

                    Logger.LogWarning(
                        "Authenticating over an unencrypted {Protocol} connection to {Host}:{Port} — " +
                        "the mail password crosses this link in the clear",
                        endpoint.Protocol, endpoint.Host, endpoint.Port);
                }

                await client.AuthenticateAsync(connection.Username, connection.Password, connectCts.Token);
            }

            var session = CreateSession(client);
            client = null; // ownership transferred to the session
            return Result.Success(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            // Never echo the server's message: it can disclose account state.
            Logger.LogWarning("{Protocol} authentication failed for {Username}", endpoint.Protocol, connection.Username);
            return Result.Failure<TSession>("Mail authentication failed");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to connect to {Protocol} at {Host}:{Port}",
                endpoint.Protocol, endpoint.Host, endpoint.Port);
            return Result.Failure<TSession>("Unable to connect to the mail service");
        }
        finally
        {
            client?.Dispose();
        }
    }

    private bool ValidateCertificate(string protocol, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;

        if (options.CurrentValue.AllowInvalidCertificate)
        {
            Logger.LogWarning("Accepting an invalid {Protocol} certificate ({Errors}) — AllowInvalidCertificate is on",
                protocol, errors);
            return true;
        }

        Logger.LogError("Rejected the {Protocol} server certificate: {Errors}", protocol, errors);
        return false;
    }
}
