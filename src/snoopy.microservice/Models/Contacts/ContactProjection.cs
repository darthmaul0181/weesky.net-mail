namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// What the child tables hold for one property line: <see cref="Position"/> is the rank of the
/// property in the card (the composer's handle), <see cref="Pref"/> the normalised PREF (1..100,
/// 101 when the card says nothing), <see cref="Params"/> the verbatim parameter block.
/// </summary>
public sealed record ProjectedLine(int Position, string Type, int Pref, string Params, string GroupName);

public sealed record ProjectedEmail(string Address, ProjectedLine Line);

public sealed record ProjectedPhone(string Number, ProjectedLine Line);

public sealed record ProjectedAddress(
    string? PoBox, string? Extended, string? Street, string? Locality, string? Region,
    string? PostalCode, string? Country, ProjectedLine Line);

public sealed record ProjectedPhoto(string MediaType, byte[] Bytes);

/// <summary>
/// One MEMBER line of a group card: <see cref="MemberUid"/> is the referenced contact's UID with
/// any <c>urn:uuid:</c> prefix removed, <see cref="Position"/> its rank in the card.
/// </summary>
public sealed record ProjectedMember(string MemberUid, int Position);

/// <summary>
/// The projection of one stored vCard — everything the database columns derive from the card.
/// Produced by <c>VCardProjector</c>, the read half of the total re-projection cycle.
/// </summary>
public sealed record ContactProjection(
    string? FirstName, string? LastName, string? Nickname,
    string? DisplayName, string? MiddleName, string? NamePrefix, string? NameSuffix,
    string? Organization, string? Department, string? JobTitle,
    string? Birthday, string? Website, string? Notes, string? Uid,
    IReadOnlyList<ProjectedEmail> Addresses,
    IReadOnlyList<ProjectedPhone> Phones,
    IReadOnlyList<ProjectedAddress> PostalAddresses,
    ProjectedPhoto? Photo,
    string Kind,
    IReadOnlyList<ProjectedMember> Members);
