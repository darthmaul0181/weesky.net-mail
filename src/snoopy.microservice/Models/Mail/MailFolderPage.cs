namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One page of a folder, newest message first.</summary>
public sealed class MailFolderPage
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// UID validity at the time of the read. When this changes, every UID the client
    /// cached for this folder is meaningless and must be discarded — otherwise the client
    /// will open the wrong messages.
    /// </summary>
    public uint UidValidity { get; set; }

    /// <summary>Total messages in the folder, all pages combined.</summary>
    public int Total { get; set; }

    /// <summary>Zero-based page index.</summary>
    public int Page { get; set; }

    public int PageSize { get; set; }

    public List<MailMessageSummary> Messages { get; set; } = new();

    /// <summary>Grouped mode only: one entry per conversation, newest thread first. Null — and
    /// therefore absent from the JSON — on a flat page, which is how the client tells the modes
    /// apart.</summary>
    public List<MailThread>? Threads { get; set; }

    /// <summary>Grouped mode only: how many conversations the folder holds — what the pager
    /// pages. Total keeps counting messages.</summary>
    public int? TotalThreads { get; set; }
}
