namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The body of PUT /api/Contacts/{id}/Favorite. A settable class, bound from JSON.</summary>
public sealed class FavoriteRequest
{
    public bool IsFavorite { get; set; }
}
