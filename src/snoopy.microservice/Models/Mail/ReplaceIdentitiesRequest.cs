namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The full replacement set — PUT semantics, so order and default are atomic.</summary>
public sealed record ReplaceIdentitiesRequest
{
    public IReadOnlyList<IdentityEntry> Identities { get; init; } = [];
}
