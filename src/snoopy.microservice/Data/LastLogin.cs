using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data
{
    [Table("last_login")]
    public class LastLogin
    {
        [Column("userid")]
        public string UserId { get; set; }      // "username@domainname"

        [Column("service")]
        public string Service { get; set; }     // "imap" or "lmtp"

        [Column("last_access")]
        public long LastAccess { get; set; }    // Unix timestamp (seconds)

        [Column("last_ip")]
        public string LastIp { get; set; } = string.Empty;
    }
}
