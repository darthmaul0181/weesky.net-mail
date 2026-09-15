using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The key the mail server presents to have a guest's reply applied at delivery, and the switch
/// that opens that door (spec 5e3). One row for the instance, never in app_settings: that table
/// is read without a session, and the switch alone would tell the Internet the door is open.
/// </summary>
[Table("delivery_reply_key")]
public sealed class DeliveryReplyKey
{
    public const byte SingletonId = 1;

    [Column("id")]
    public byte Id { get; set; } = SingletonId;

    /// <summary>SHA-256 of the key. Never the key.</summary>
    [Column("key_hash")]
    public byte[] KeyHash { get; set; } = [];

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC; the last call whose key matched. Null again after every generation.</summary>
    [Column("last_call_at")]
    public DateTime? LastCallAt { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; }

    /// <summary>UTC. The concurrency token of the admin screen's writes; a recorded call leaves it alone.</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
