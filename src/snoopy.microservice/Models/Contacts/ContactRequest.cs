using System.Text.Json.Serialization;

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

    public string? DisplayName { get; set; }

    public string? MiddleName { get; set; }

    public string? NamePrefix { get; set; }

    public string? NameSuffix { get; set; }

    public string? Organization { get; set; }

    public string? Department { get; set; }

    public string? JobTitle { get; set; }

    public string? Birthday { get; set; }

    public string? Website { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Absent or null: the card keeps its photo. Empty: removed. Otherwise base64 with no data:
    /// prefix and no media type — the bytes are what say the format (décision 3).
    /// </summary>
    public string? Photo { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>
    /// Ordered; the first surviving entry becomes the primary address. Accepts a bare string —
    /// the shape every current screen still sends — or an object naming a position and a type;
    /// see <see cref="ContactLineJsonConverter"/>.
    /// </summary>
    [JsonConverter(typeof(ContactLineJsonConverter))]
    public List<ContactEmailPayload>? Addresses { get; set; }

    public List<ContactPhonePayload>? Phones { get; set; }

    public List<ContactAddressPayload>? PostalAddresses { get; set; }

    /// <summary>Where the card came from. Absent or unknown is filed as "manual".</summary>
    public string? Source { get; set; }

    /// <summary>
    /// The <see cref="ContactDetail.CardHash"/> the editor read before this write. Absent writes as
    /// before this field existed; present and no longer the stored hash refuses the write with 409.
    /// </summary>
    public string? CardHash { get; set; }
}
