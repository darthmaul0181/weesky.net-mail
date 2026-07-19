using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Opens one IMAP connection per request — no pooling, the Rainloop model. Modelled on
    /// ManageSieveClient.OpenSessionAsync: guard on unconfigured options, a generic message to
    /// the client with the detail logged, and ownership of the client transferred to the
    /// session on success so the finally block is a no-op on the happy path.
    ///
    /// Options are read through IOptionsMonitor, not IOptions, so a correction in
    /// appsettings.json takes effect without restarting the service and dropping live sessions.
    /// </summary>
    public class ImapConnectionFactory : IImapConnectionFactory
    {
        private readonly IOptionsMonitor<MailOptions> _options;
        private readonly ILogger<ImapConnectionFactory> _logger;

        public ImapConnectionFactory(IOptionsMonitor<MailOptions> options, ILogger<ImapConnectionFactory> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task<Result<IImapSession>> OpenAsync(string email, string password, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));

            var options = _options.CurrentValue;

            if (!options.IsImapConfigured)
            {
                _logger.LogError("IMAP is not configured (Mail:ImapHost missing)");
                return Result.Failure<IImapSession>("Mail service is not configured");
            }

            ImapClient? client = null;

            try
            {
                client = new ImapClient
                {
                    ServerCertificateValidationCallback = ValidateCertificate,
                    Timeout = options.TimeoutSeconds * 1000
                };

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                    await client.ConnectAsync(options.ImapHost, options.ImapPort, options.ImapSecurity, connectCts.Token);
                    await client.AuthenticateAsync(email, password, connectCts.Token);
                }

                var session = new ImapSession(client, _logger);
                client = null; // ownership transferred to the session
                return Result.Success<IImapSession>(session);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AuthenticationException)
            {
                // Never echo the server's message: it can disclose account state.
                _logger.LogWarning("IMAP authentication failed for {Email}", email);
                return Result.Failure<IImapSession>("Mail authentication failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to connect to IMAP at {Host}:{Port}", options.ImapHost, options.ImapPort);
                return Result.Failure<IImapSession>("Unable to connect to the mail service");
            }
            finally
            {
                client?.Dispose();
            }
        }

        private bool ValidateCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None) return true;

            if (_options.CurrentValue.AllowInvalidCertificate)
            {
                _logger.LogWarning("Accepting an invalid IMAP certificate ({Errors}) — AllowInvalidCertificate is on", errors);
                return true;
            }

            _logger.LogError("Rejected the IMAP server certificate: {Errors}", errors);
            return false;
        }
    }
}
