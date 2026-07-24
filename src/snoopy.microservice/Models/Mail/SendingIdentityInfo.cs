namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// One resolved sending identity. LabelIsCustom tells the client whether the label comes from a
/// stored row (true) or from the account's live FullName — that flag decides whether the primary
/// belongs in a PUT payload.
/// </summary>
public sealed record SendingIdentityInfo(
    string Address, string DisplayName, bool IsDefault, bool IsPrimary, bool Stale, bool LabelIsCustom);
