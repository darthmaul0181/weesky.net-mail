using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What OpenDraft resumes: the folder and UID of the stored draft.</summary>
public sealed record OpenDraftRequest(
    [property: Required(ErrorMessage = "A folder is required")] string Folder,
    uint Uid);
