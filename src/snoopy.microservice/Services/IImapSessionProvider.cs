using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The authenticated IMAP session for the current request.
///
/// Distinct from <see cref="IImapConnectionFactory"/>, which opens a new connection on every
/// call: this hands back the same one for the whole request. One HTTP call routinely needs
/// several operations — a rename checks the folder tree first, a send files a Sent copy after
/// the SMTP hand-off — and each one used to pay its own TCP handshake, TLS negotiation and SASL
/// round trip against the mail server.
/// </summary>
public interface IImapSessionProvider
{
    /// <summary>
    /// The request's session, opening it on first use. A failure is remembered too: one
    /// refused authentication must not be retried once per operation in the same request.
    /// </summary>
    Task<Result<IImapSession>> GetAsync(MailAccountConnection connection, CancellationToken cancellationToken);
}

/// <summary>
/// Runs one operation against the request's session. The whole body of every repository method
/// on the mail path, so a caller writes the operation and nothing else.
/// </summary>
internal static class ImapSessionProviderExtensions
{
    public static async Task<Result<T>> WithSessionAsync<T>(
        this IImapSessionProvider provider, MailAccountConnection connection,
        Func<IImapSession, Task<Result<T>>> operation, CancellationToken cancellationToken)
    {
        var session = await provider.GetAsync(connection, cancellationToken);
        return session.IsFailure
            ? Result.Failure<T>(session.Error)
            : await operation(session.Value);
    }

    public static async Task<Result> WithSessionAsync(
        this IImapSessionProvider provider, MailAccountConnection connection,
        Func<IImapSession, Task<Result>> operation, CancellationToken cancellationToken)
    {
        var session = await provider.GetAsync(connection, cancellationToken);
        return session.IsFailure
            ? Result.Failure(session.Error)
            : await operation(session.Value);
    }
}
