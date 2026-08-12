namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The batch, plus the flag it is being given.</summary>
public sealed record BulkFavoriteRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];

    public bool IsFavorite { get; init; }
}
