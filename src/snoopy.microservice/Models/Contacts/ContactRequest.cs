namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// The body of POST /api/Contacts and PUT /api/Contacts/{id}. A settable class rather than a
/// record: it is bound from JSON, and every field is optional at the wire level so
/// <see cref="Services.ContactValidator"/> can answer one clear message instead of the binder
/// answering several unclear ones.
/// </summary>
public sealed class ContactRequest
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Nickname { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>Ordered; the first surviving entry becomes the primary address.</summary>
    public List<string>? Addresses { get; set; }

    /// <summary>Where the card came from. Absent or unknown is filed as "manual".</summary>
    public string? Source { get; set; }
}
