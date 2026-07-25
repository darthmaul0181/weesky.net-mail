namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The resolved sending identities, primary always included.</summary>
public sealed record IdentityListResponse(IReadOnlyList<SendingIdentityInfo> Identities);
