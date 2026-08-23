using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The one synchronisation secret an account has. <c>user_id</c> is the primary key rather than a
/// surrogate: that is the shape saying there is one secret per person and not two, and a table
/// keyed otherwise would accept a second row nothing in this code creates — until a restore put
/// one there.
///
/// Absent row means never enabled; <see cref="CardDavEnabled"/> false means switched off but still
/// configured, which is a different answer at the edge (403, never 401) and a different gesture on
/// screen. The secret itself is never stored — only the salted digest of it.
/// </summary>
[Table("dav_credentials")]
public sealed class DavCredential
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>Per protocol, not per secret: CalDAV gets a column of its own, never a migration.</summary>
    [Column("carddav_enabled")]
    public bool CardDavEnabled { get; set; } = true;

    /// <summary>Lower-case hexadecimal SHA-256 of <c>salt ‖ UTF8(secret)</c>. 64 characters.</summary>
    [Column("secret_hash")]
    public string SecretHash { get; set; } = string.Empty;

    [Column("salt")]
    public byte[] Salt { get; set; } = [];

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Null until a client authenticates. Written at most once an hour, per instance.</summary>
    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
