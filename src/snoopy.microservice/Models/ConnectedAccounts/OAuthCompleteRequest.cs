namespace weesky.Snoopy.Microservice.Models.ConnectedAccounts;

public sealed record OAuthCompleteRequest(string State)
{
    /// <summary>Redacted: the state is a live handshake handle the generated ToString would print.</summary>
    public override string ToString() => "OAuthCompleteRequest";
}
