using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Security;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Where and how to reach one of the mail services, read from <see cref="MailOptions"/>.
/// <c>Protocol</c> names it in log messages ("IMAP"), and <c>ConfigurationKey</c> is the setting
/// whose absence means "not configured", so the log line points at what to fill in.
/// </summary>
internal readonly record struct MailEndpoint(
    string Protocol, string ConfigurationKey, string Host, int Port, SecureSocketOptions Security, bool IsConfigured);

/// <summary>
/// Opens one connection per request — no pooling, the Rainloop model: guard on unconfigured
/// options, a generic message to the client with the detail logged, and ownership of the client
/// transferred to the session on success so the finally block is a no-op on the happy path.
///
/// Shared by the IMAP and SMTP factories, which were the same file twice down to a byte-identical
/// certificate callback — including the rule that matters most here, that an authentication
/// failure must never echo the server's message back to the caller.
///
/// Options are read through IOptionsMonitor, not IOptions, so a correction in appsettings.json
/// takes effect without restarting the service and dropping live sessions.
/// </summary>
internal abstract class MailConnectionFactory<TClient, TSession>(
    IOptionsMonitor<MailOptions> options, ILogger logger)
    where TClient : MailService
{
    protected ILogger Logger { get; } = logger;

    protected abstract MailEndpoint Endpoint(MailOptions options);

    protected abstract TClient CreateClient();

    /// <summary>Wraps the connected client. The session owns it from here, disposal included.</summary>
    protected abstract TSession CreateSession(TClient client);

    public async Task<Result<TSession>> OpenAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));

        var endpoint = Endpoint(options.CurrentValue);

        if (!endpoint.IsConfigured)
        {
            Logger.LogError("{Protocol} is not configured ({ConfigurationKey} missing)",
                endpoint.Protocol, endpoint.ConfigurationKey);
            return Result.Failure<TSession>("Mail service is not configured");
        }

        TClient? client = null;

        try
        {
            client = CreateClient();
            client.ServerCertificateValidationCallback = ValidateCertificate;
            client.Timeout = options.CurrentValue.TimeoutSeconds * 1000;

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(options.CurrentValue.TimeoutSeconds));
                await client.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.Security, connectCts.Token);
                await client.AuthenticateAsync(email, password, connectCts.Token);
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
            Logger.LogWarning("{Protocol} authentication failed for {Email}", endpoint.Protocol, email);
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

    private bool ValidateCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;

        var protocol = Endpoint(options.CurrentValue).Protocol;

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
