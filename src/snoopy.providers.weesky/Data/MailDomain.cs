using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace weesky.Snoopy.Providers.Weesky.Data;

[Table("domains")]
[DebuggerDisplay("{Name} ({Id})")]
public sealed class MailDomain
{
    [Key]
    [Column("id")]
    [StringLength(3)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    [StringLength(30)]
    public string Name { get; set; } = string.Empty;
}
