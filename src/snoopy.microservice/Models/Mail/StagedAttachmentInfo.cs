namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What the upload endpoint answers and the compose client holds on to.</summary>
public sealed record StagedAttachmentInfo(Guid Id, string FileName, long Size, string ContentType);

/// <summary>A staged file resolved for sending. The path stays inside the store's root.</summary>
public sealed record StagedAttachment(StagedAttachmentInfo Info, string FilePath);
