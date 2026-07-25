namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A draft save: the send shape plus the previous version this one replaces.</summary>
public record SaveDraftRequest : SendMessageRequest
{
    /// <summary>UID of the superseded version — expunged once the new one is in place.</summary>
    public uint? ReplaceUid { get; init; }
}
