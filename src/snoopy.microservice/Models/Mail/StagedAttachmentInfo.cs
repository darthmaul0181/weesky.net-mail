namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What the upload endpoint answers and the compose client holds on to.
/// A non-null ContentId marks an inline body resource (cid part) to pack as multipart/related.</summary>
public sealed record StagedAttachmentInfo(Guid Id, string FileName, long Size, string ContentType, string? ContentId = null);

/// <summary>A staged file resolved for sending. The path stays inside the store's root.</summary>
public sealed record StagedAttachment(StagedAttachmentInfo Info, string FilePath);
