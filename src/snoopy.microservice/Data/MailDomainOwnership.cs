using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace weesky.Snoopy.Microservice.Data;

[Table("domains_ownerships")]
[DebuggerDisplay("{DomainId} owned by user {UserId}")]
public sealed class MailDomainOwnership
{
    [Required]
    [StringLength(3)]
    [Column("domainId")]
    public string DomainId { get; set; }

    [Required]
    [Column("userId")]
    public int UserId { get; set; }
}
