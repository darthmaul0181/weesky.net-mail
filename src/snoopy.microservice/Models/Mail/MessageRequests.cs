namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// Batch from day one — multi-select (2b3) reuses this unchanged. The folder path travels
/// in the body, never in a route segment: the hierarchy separator may be '/'.
/// </summary>
public sealed class SetMessageFlagsRequest
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>1 to 200 entries — the same ceiling as pageSize.</summary>
    public IReadOnlyList<uint> Uids { get; set; } = [];

    public MailFlag Flag { get; set; }

    /// <summary>True sets the flag, false clears it.</summary>
    public bool Value { get; set; }
}
