using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Providers.Weesky.Data;

[Table("last_login")]
public sealed class LastLogin
{
    [Column("userid")]
    public string UserId { get; set; } = string.Empty;      // "username@domainname"

    [Column("service")]
    public string Service { get; set; } = string.Empty;     // "imap" or "lmtp"

    [Column("last_access")]
    public long LastAccess { get; set; }    // Unix timestamp (seconds)

}
