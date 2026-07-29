using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Builds the outgoing MimeMessage Send and Drafts share: validated From, staged attachments
/// resolved, outbound-sanitised body with staged URLs rewritten to cid, safe threading headers.
/// </summary>
internal interface IOutgoingMessageFactory
{
    /// <summary>Returned as the failure when a staged attachment id no longer resolves.</summary>
    const string UnknownAttachment = "unknown_attachment";

    /// <summary>
    /// Returned when the requested From is not one the sending account owns: for the primary,
    /// neither its address nor a live alias; for a connected account, neither its own login
    /// address nor an identity stored against it.
    /// </summary>
    const string ForbiddenFrom = "forbidden_from";

    /// <summary>
    /// Builds the complete outgoing message for the request, or fails with
    /// <see cref="UnknownAttachment"/> / <see cref="ForbiddenFrom"/> — never a partial message.
    /// <paramref name="connection"/> names the sending account: it decides both the staged
    /// namespace and which addresses the From may carry.
    /// </summary>
    Task<Result<MimeMessage>> CreateAsync(
        User user, MailAccountConnection connection, SendMessageRequest request, CancellationToken cancellationToken);
}
