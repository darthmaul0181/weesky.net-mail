using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The SMTP half of <see cref="MailConnectionFactory{TClient,TSession}"/>. Submission uses the
/// account's own credentials, the same ones the IMAP side reads from the connection record.
/// </summary>
internal sealed class SmtpConnectionFactory(
    IOptionsMonitor<MailOptions> options, ILogger<SmtpConnectionFactory> logger)
    : MailConnectionFactory<SmtpClient, ISmtpSession>(options, logger), ISmtpConnectionFactory
{
    protected override MailEndpoint Endpoint(MailAccountConnection connection) => new(
        Protocol: "SMTP",
        ConfigurationKey: "Mail:SmtpHost",
        Host: connection.SmtpHost,
        Port: connection.SmtpPort,
        Security: connection.SmtpSecurity,
        IsConfigured: !string.IsNullOrWhiteSpace(connection.SmtpHost));

    protected override SmtpClient CreateClient() => new();

    protected override ISmtpSession CreateSession(SmtpClient client) => new SmtpSession(client, Logger);

    Task<Result<ISmtpSession>> ISmtpConnectionFactory.OpenAsync(
        MailAccountConnection connection, CancellationToken cancellationToken) =>
        OpenAsync(connection, cancellationToken);
}
