using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The SMTP half of <see cref="MailConnectionFactory{TClient,TSession}"/>. Submission uses the
/// account's own credentials, the same ones the IMAP side reads from the credentials cookie.
/// </summary>
internal sealed class SmtpConnectionFactory(
    IOptionsMonitor<MailOptions> options, ILogger<SmtpConnectionFactory> logger)
    : MailConnectionFactory<SmtpClient, ISmtpSession>(options, logger), ISmtpConnectionFactory
{
    protected override MailEndpoint Endpoint(MailOptions options) => new(
        Protocol: "SMTP",
        ConfigurationKey: "Mail:SmtpHost",
        Host: options.SmtpHost,
        Port: options.SmtpPort,
        Security: options.SmtpSecurity,
        IsConfigured: options.IsSmtpConfigured);

    protected override SmtpClient CreateClient() => new();

    protected override ISmtpSession CreateSession(SmtpClient client) => new SmtpSession(client, Logger);

    Task<Result<ISmtpSession>> ISmtpConnectionFactory.OpenAsync(
        string email, string password, CancellationToken cancellationToken) =>
        OpenAsync(email, password, cancellationToken);
}
