using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace weesky.Snoopy.Microservice.Data;

[Table("aliases")]
[DebuggerDisplay("{SourceName}@{SourceDomainId} -> user {DestinationUserId}")]
public sealed class MailAlias
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [StringLength(30)]
    [Column("source_addr")]
    public string Name { get; set; }

    [Required]
    [StringLength(3)]
    [Column("source_domain")]
    public string Domain { get; set; }

    [Required]
    [Column("destination_user")]
    public int DestinationUserId { get; set; }

}
