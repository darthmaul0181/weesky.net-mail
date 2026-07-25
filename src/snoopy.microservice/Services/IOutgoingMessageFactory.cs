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

    /// <summary>Returned when the requested From is neither the primary address nor a live alias.</summary>
    const string ForbiddenFrom = "forbidden_from";

    Task<Result<MimeMessage>> CreateAsync(
        User user, SendMessageRequest request, CancellationToken cancellationToken);
}
