using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Scotty.Microservice.Data.Preferences;

/// <summary>One rendition of the instance logo; no row at all means the bundled Scotty logo.</summary>
[Table("app_logo")]
public sealed class AppLogoImage
{
    [Column("size")]
    public short Size { get; set; }

    [Column("image")]
    public byte[] Image { get; set; } = [];

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
