using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What OpenDraft resumes: the folder and UID of the stored draft.</summary>
/// <remarks>
/// Properties rather than a primary constructor, like PrepareQuoteRequest: MVC refuses to bind
/// a record carrying validation metadata on a constructor-generated property.
/// </remarks>
public sealed record OpenDraftRequest
{
    [Required(ErrorMessage = "A folder is required")]
    public string Folder { get; init; } = string.Empty;

    public uint Uid { get; init; }
}
