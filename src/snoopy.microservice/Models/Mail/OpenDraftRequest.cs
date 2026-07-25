namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What OpenDraft resumes: the folder and UID of the stored draft.</summary>
public sealed record OpenDraftRequest(string Folder, uint Uid);
