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
