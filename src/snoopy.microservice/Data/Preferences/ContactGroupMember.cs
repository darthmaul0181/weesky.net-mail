using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>One MEMBER entry of one group card. Flat like its contact sibling tables.</summary>
[Table("contact_group_members")]
public sealed class ContactGroupMember
{
    [Column("group_id")]
    public Guid GroupId { get; set; }

    /// <summary>The member's UID, its urn:uuid: prefix stripped — never its id: a client may PUT
    /// the group before its members, so the reference is allowed to dangle (décision 2).</summary>
    [Column("member_uid")]
    public string MemberUid { get; set; } = string.Empty;

    /// <summary>Rank of the MEMBER property in the card; holes are legal (décision 3).</summary>
    [Column("position")]
    public int Position { get; set; }
}
