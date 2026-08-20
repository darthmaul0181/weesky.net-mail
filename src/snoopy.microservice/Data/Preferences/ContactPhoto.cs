using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The first raster-image PHOTO occurrence of one contact, projected so serving an avatar does
/// not require loading the whole card. Kept out of <c>GET /api/Contacts</c>; served by its own route.
/// </summary>
[Table("contact_photos")]
public sealed class ContactPhoto
{
    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [Column("bytes")]
    public byte[] Bytes { get; set; } = [];
}
