namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The batch a bulk contact write names. Empty and over-cap are both refused.</summary>
public sealed record BulkContactsRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
}
