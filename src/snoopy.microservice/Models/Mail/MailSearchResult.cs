namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// One search hit: a summary plus where it lives. In all-folders scope each row must name
/// its folder; in single-folder scope they are uniform but the shape stays one.
/// </summary>
public sealed class MailSearchResult : MailMessageSummary
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>UID validity of that folder at search time — the result is a snapshot.</summary>
    public uint UidValidity { get; set; }
}
