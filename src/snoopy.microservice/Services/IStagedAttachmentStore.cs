using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Temporary store for outgoing attachments, the Rainloop model: uploaded on add, referenced
/// by id at send time. Ids are sealed to the account that created them.
/// </summary>
public interface IStagedAttachmentStore
{
    /// <summary>Streams one upload to disk. Fails when the file or the account total exceeds the caps.
    /// A contentId marks the file as an inline body resource rather than a plain attachment.</summary>
    Task<Result<StagedAttachmentInfo>> SaveAsync(string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken, string? contentId = null);

    /// <summary>Resolves one staged file. An unknown or foreign id is a plain failure.</summary>
    Result<StagedAttachment> Open(string accountId, Guid id);

    /// <summary>Removes one staged file. Removing what is already gone is a no-op.</summary>
    void Delete(string accountId, Guid id);

    /// <summary>Drops entries older than the TTL; answers how many went.</summary>
    int SweepExpired();
}
