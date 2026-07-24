using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Opens one SMTP connection per request, the same model as ImapConnectionFactory: the
/// user's own password from the credentials cookie, options through IOptionsMonitor so a
/// correction in appsettings.json applies without a restart.
/// </summary>
internal sealed class SmtpConnectionFactory : ISmtpConnectionFactory
{
    private readonly IOptionsMonitor<MailOptions> _options;
    private readonly ILogger<SmtpConnectionFactory> _logger;

    public SmtpConnectionFactory(IOptionsMonitor<MailOptions> options, ILogger<SmtpConnectionFactory> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<Result<ISmtpSession>> OpenAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));

        var options = _options.CurrentValue;

        if (!options.IsSmtpConfigured)
        {
            _logger.LogError("SMTP is not configured (Mail:SmtpHost missing)");
            return Result.Failure<ISmtpSession>("Mail service is not configured");
        }

        SmtpClient? client = null;

        try
        {
            client = new SmtpClient
            {
                ServerCertificateValidationCallback = ValidateCertificate,
                Timeout = options.TimeoutSeconds * 1000
            };

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                await client.ConnectAsync(options.SmtpHost, options.SmtpPort, options.SmtpSecurity, connectCts.Token);
                await client.AuthenticateAsync(email, password, connectCts.Token);
            }

            var session = new SmtpSession(client, _logger);
            client = null; // ownership transferred to the session
            return Result.Success<ISmtpSession>(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            // Never echo the server's message: it can disclose account state.
            _logger.LogWarning("SMTP authentication failed for {Email}", email);
            return Result.Failure<ISmtpSession>("Mail authentication failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to connect to SMTP at {Host}:{Port}", options.SmtpHost, options.SmtpPort);
            return Result.Failure<ISmtpSession>("Unable to connect to the mail service");
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
            _logger.LogWarning("Accepting an invalid SMTP certificate ({Errors}) — AllowInvalidCertificate is on", errors);
            return true;
        }

        _logger.LogError("Rejected the SMTP server certificate: {Errors}", errors);
        return false;
    }
}
