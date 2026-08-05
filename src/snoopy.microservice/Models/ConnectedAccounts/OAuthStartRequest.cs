namespace weesky.Snoopy.Microservice.Models.ConnectedAccounts;

/// <summary>Exactly one of the two: a domain to attach a new mailbox from, or an account to
/// re-authenticate.</summary>
public sealed record OAuthStartRequest(Guid? DomainId, Guid? AccountId)
{
    /// <summary>Explicit like every request DTO here, so the redaction pattern has no gap.</summary>
    public override string ToString() => $"OAuthStartRequest (domain={DomainId}, account={AccountId})";
}
