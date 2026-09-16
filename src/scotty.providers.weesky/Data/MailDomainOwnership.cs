using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace weesky.Scotty.Providers.Weesky.Data;

[Table("domains_ownerships")]
[DebuggerDisplay("{DomainId} owned by user {UserId}")]
public sealed class MailDomainOwnership
{
    [Required]
    [StringLength(3)]
    [Column("domainId")]
    public string DomainId { get; set; } = string.Empty;

    [Required]
    [Column("userId")]
    public int UserId { get; set; }
}
