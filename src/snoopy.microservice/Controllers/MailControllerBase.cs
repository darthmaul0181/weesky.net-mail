using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Shared spine of the api/Mail controllers — IMAP access on the account the request names.
///
/// Three conventions hold across every action. Folder paths never appear in a route
/// segment — the hierarchy separator may be '/', which would break routing — so they
/// travel in the query string or the request body. Every action resolves the active
/// account first, so nothing ever serves the primary mailbox while the user is looking at
/// a connected one. And the failure modes are distinct: a missing or undecryptable
/// credentials cookie is 401 ("credentials_unavailable") so the client can sign in again,
/// an unknown or foreign account is 404, a connected account whose stored secret no longer
/// decrypts is 409, and anything the mail server refuses is 502.
/// </summary>
public abstract class MailControllerBase(IAccountConnectionResolver connections) : ApiBaseController
{
    /// <summary>
    /// The active account's connection — endpoints plus credentials — or the error to answer
    /// with. Every mail action reads a mailbox as the user, so every one of them starts here.
    /// A guard rather than an action filter on purpose: the controller tests invoke actions
    /// directly and no filter would run, so moving the check into the pipeline would move it
    /// out of the tests that cover it.
    /// </summary>
    protected async Task<AccountResolution<MailAccountConnection>> TryResolveAsync(
        CancellationToken cancellationToken)
    {
        var resolved = await connections.ResolveAsync(AuthenticatedUser, Request, cancellationToken);
        return resolved.IsSuccess
            ? AccountResolution<MailAccountConnection>.Success(resolved.Value)
            : AccountResolution<MailAccountConnection>.Failure(ConnectedAccountError(resolved.Error));
    }

    /// <summary>
    /// The three sentinels that name a thing that is not there, against the mail server merely
    /// refusing. Rule 4 keeps them constants precisely so the layer producing an error and the
    /// layer choosing a status cannot drift apart, which is what a per-call spelling would allow.
    /// </summary>
    protected static bool IsMissing(string error) =>
        error is ImapSession.FolderNotFound or ImapSession.MessageNotFound or ImapSession.AttachmentNotFound;
}
