namespace weesky.Snoopy.Microservice.Models;

public sealed class SieveAction
{
    public SieveActionType Type { get; set; }

    /// <summary>
    /// FileInto → folder name. Redirect → email address. Reject → reason text. SetFlag → flag name (e.g. <c>\Seen</c>).
    /// Unused for Discard / Keep.
    /// </summary>
    public string? Argument { get; set; }

    /// <summary>
    /// FileInto only — when <c>true</c>, emits <c>fileinto :create "folder"</c> and adds <c>"mailbox"</c> to
    /// <c>require[]</c>. Creates the target folder if it does not already exist (RFC 5490).
    /// </summary>
    public bool AutoCreate { get; set; }
}
