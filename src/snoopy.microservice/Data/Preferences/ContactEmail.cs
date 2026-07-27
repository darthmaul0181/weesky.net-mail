using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One address of one contact. Stored canonical (trimmed, lower-case): the table collates in
/// binary, so a casing difference would split one address into two rows. <see cref="Position"/>
/// carries the order, and position 0 is the primary address by definition — there is no flag to
/// keep in step with it.
/// </summary>
[Table("contact_emails")]
public sealed class ContactEmail
{
    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("position")]
    public int Position { get; set; }
}
