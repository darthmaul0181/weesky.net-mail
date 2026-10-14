using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Scotty.Microservice.Data.Preferences;

/// <summary>
/// What an account has done with one novelty of the web app's catalogue. The server knows no
/// catalogue: an id it never heard of is stored like any other, and the web app ignores rows whose
/// id it no longer ships.
/// </summary>
[Table("whats_new_items")]
public sealed class WhatsNewItem
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("item_id")]
    public string ItemId { get; set; } = string.Empty;

    [Column("first_shown_at")]
    public DateTime? FirstShownAt { get; set; }

    [Column("listed_at")]
    public DateTime? ListedAt { get; set; }

    [Column("seen_at")]
    public DateTime? SeenAt { get; set; }
}
