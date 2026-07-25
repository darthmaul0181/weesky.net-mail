namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One identity as the client submits it. Defaults absorb explicit JSON nulls.</summary>
public sealed record IdentityEntry
{
    public string Address { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}
