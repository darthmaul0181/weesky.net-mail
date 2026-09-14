using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The mail carrying a guest's REPLY (spec 5e, décision 12). The file itself never travels:
/// the server re-reads it from IMAP by <see cref="Folder"/>/<see cref="Uid"/>/<see cref="Part"/>.</summary>
public sealed class ApplyReplyRequest
{
    [Required(ErrorMessage = "A folder is required")]
    public string Folder { get; set; } = string.Empty;

    public uint Uid { get; set; }

    /// <summary>The MIME part specifier the detail carried. Empty is a real specifier.</summary>
    [Required(AllowEmptyStrings = true)]
    public string Part { get; set; } = string.Empty;
}
