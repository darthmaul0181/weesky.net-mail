using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The pool's view of the factory: a bare connected client, and a session over a client it did
/// not open. Separate from <see cref="IImapConnectionFactory"/> on purpose — the login probe and
/// the connected-account probe must keep authenticating for real, and only ever see that one.
/// </summary>
internal interface IImapClientSource
{
    /// <summary>A connected, authenticated client the caller owns. Same failures as OpenAsync.</summary>
    Task<Result<ImapClient>> OpenClientAsync(MailAccountConnection connection, CancellationToken cancellationToken);

    /// <summary>Wraps a client; the session calls <paramref name="release"/> exactly once, on disposal.</summary>
    IImapSession CreateSession(ImapClient client, ImapClientRelease release);
}
