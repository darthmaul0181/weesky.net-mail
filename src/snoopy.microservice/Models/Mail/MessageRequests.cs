using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// Batch from day one — multi-select (2b3) reuses this unchanged. The folder path travels
/// in the body, never in a route segment: the hierarchy separator may be '/'.
/// </summary>
public abstract class MessageBatchRequest
{
    private IReadOnlyList<uint> _uids = [];

    [Required(ErrorMessage = "A folder is required")]
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// 1 to 200 entries — the same ceiling as pageSize. The setter coalesces because an
    /// initialiser only covers an *absent* property: a body carrying <c>"uids": null</c>
    /// overwrites it, and the controller's count check then threw a 500 on a bad request.
    /// </summary>
    [MinLength(1, ErrorMessage = "Uids must hold between 1 and 200 entries")]
    [MaxLength(200, ErrorMessage = "Uids must hold between 1 and 200 entries")]
    public IReadOnlyList<uint> Uids
    {
        get => _uids;
        set => _uids = value ?? [];
    }
}

public sealed class SetMessageFlagsRequest : MessageBatchRequest
{
    public MailFlag Flag { get; set; }

    /// <summary>True sets the flag, false clears it.</summary>
    public bool Value { get; set; }
}

public sealed class MoveMessagesRequest : MessageBatchRequest
{
    [Required(ErrorMessage = "A target folder is required")]
    public string TargetFolderPath { get; set; } = string.Empty;
}

public sealed class DeleteMessagesRequest : MessageBatchRequest;

/// <summary>
/// Empties an entire folder. Unbounded by the 200-UID cap — it operates on 1:* server-side.
/// A null/blank <see cref="TargetFolderPath"/> means purge (permanent expunge of everything);
/// a target means move every message there (used to move a normal folder's contents to trash).
/// </summary>
public sealed class EmptyFolderRequest
{
    [Required(ErrorMessage = "A folder is required")]
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
    [Required(ErrorMessage = "A folder is required")]
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

    [Range(0, int.MaxValue, ErrorMessage = "Page must not be negative")]
    public int Page { get; set; }

    [Range(1, 200, ErrorMessage = "Page size must be between 1 and 200")]
    public int PageSize { get; set; } = 50;
}
