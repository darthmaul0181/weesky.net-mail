using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The IMAP half of <see cref="MailConnectionFactory{TClient,TSession}"/> — where to connect and
/// what to wrap the connection in. Everything else (timeout, certificate policy, the guard on
/// unconfigured options, the ownership hand-off) lives in the base.
/// </summary>
internal sealed class ImapConnectionFactory(
    IOptionsMonitor<MailOptions> options,
    IMailHtmlSanitizer sanitizer,
    ILogger<ImapConnectionFactory> logger)
    : MailConnectionFactory<ImapClient, IImapSession>(options, logger), IImapConnectionFactory
{
    protected override MailEndpoint Endpoint(MailOptions options) => new(
        Protocol: "IMAP",
        ConfigurationKey: "Mail:ImapHost",
        Host: options.ImapHost,
        Port: options.ImapPort,
        Security: options.ImapSecurity,
        IsConfigured: options.IsImapConfigured);

    protected override ImapClient CreateClient() => new();

    protected override IImapSession CreateSession(ImapClient client) => new ImapSession(client, sanitizer, Logger);

    Task<Result<IImapSession>> IImapConnectionFactory.OpenAsync(
        string email, string password, CancellationToken cancellationToken) =>
        OpenAsync(email, password, cancellationToken);
}
