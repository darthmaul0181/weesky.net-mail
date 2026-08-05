namespace weesky.Snoopy.Microservice.Models.ConnectedAccounts;

/// <summary>The URL the client navigates to, and the handle the callback will hand back.</summary>
public sealed record OAuthStartResponse(string AuthorizationUrl, string State);
