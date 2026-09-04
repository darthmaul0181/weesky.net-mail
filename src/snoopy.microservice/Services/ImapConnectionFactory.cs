using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The IMAP half of <see cref="MailConnectionFactory{TClient,TSession}"/> — where to connect and
/// what to wrap the connection in. Everything else (timeout, certificate policy, the guard on
/// an unconfigured endpoint, the ownership hand-off) lives in the base.
/// </summary>
internal sealed class ImapConnectionFactory(
    IOptionsMonitor<MailOptions> options,
    IMailHtmlSanitizer sanitizer,
    ILogger<ImapConnectionFactory> logger)
    : MailConnectionFactory<ImapClient, IImapSession>(options, logger), IImapConnectionFactory, IImapClientSource
{
    protected override MailEndpoint Endpoint(MailAccountConnection connection) => new(
        Protocol: "IMAP",
        ConfigurationKey: "Mail:ImapHost",
        Host: connection.ImapHost,
        Port: connection.ImapPort,
        Security: connection.ImapSecurity,
        IsConfigured: !string.IsNullOrWhiteSpace(connection.ImapHost));

    protected override ImapClient CreateClient() => new();

    protected override IImapSession CreateSession(ImapClient client) => new ImapSession(client, sanitizer, Logger);

    Task<Result<IImapSession>> IImapConnectionFactory.OpenAsync(
        MailAccountConnection connection, CancellationToken cancellationToken) =>
        OpenAsync(connection, cancellationToken);

    Task<Result<ImapClient>> IImapClientSource.OpenClientAsync(
        MailAccountConnection connection, CancellationToken cancellationToken) =>
        OpenClientAsync(connection, cancellationToken);

    IImapSession IImapClientSource.CreateSession(ImapClient client, ImapClientRelease release) =>
        new ImapSession(client, sanitizer, Logger, release);
}
