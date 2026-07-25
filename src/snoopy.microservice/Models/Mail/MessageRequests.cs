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

public sealed class MoveMessagesRequest
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>1 to 200 entries — the same ceiling as pageSize.</summary>
    public IReadOnlyList<uint> Uids { get; set; } = [];

    public string TargetFolderPath { get; set; } = string.Empty;
}

public sealed class DeleteMessagesRequest
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>1 to 200 entries — the same ceiling as pageSize.</summary>
    public IReadOnlyList<uint> Uids { get; set; } = [];
}

/// <summary>
/// Empties an entire folder. Unbounded by the 200-UID cap — it operates on 1:* server-side.
/// A null/blank <see cref="TargetFolderPath"/> means purge (permanent expunge of everything);
/// a target means move every message there (used to move a normal folder's contents to trash).
/// </summary>
public sealed class EmptyFolderRequest
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Null or blank = purge. Set = move all messages into this folder.</summary>
    public string? TargetFolderPath { get; set; }
}

/// <summary>
/// Search criteria plus paging. FolderPath is required even when AllFolders is set — it
/// names the folder the user searched from. Quick is the fast bar (subject OR sender).
/// </summary>
public sealed class SearchMessagesRequest
{
    public string FolderPath { get; set; } = string.Empty;
    public bool AllFolders { get; set; }
    public string? Quick { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Subject { get; set; }
    public string? Text { get; set; }
    /// <summary>Compiled server-side to SINCE (today - N): the client never sends a literal date.</summary>
    public int? SinceDays { get; set; }
    public bool Unread { get; set; }
    public bool Flagged { get; set; }
    public bool HasAttachment { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; } = 50;
}
