namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>A validated write-side e-mail line. <c>Position</c> null = a new line (decision 4).</summary>
public sealed record ContactWriteEmail(int? Position, string Address, string Type);

/// <summary>A validated write-side phone line.</summary>
public sealed record ContactWritePhone(int? Position, string Number, string Type);

/// <summary>A validated write-side postal address line.</summary>
public sealed record ContactWriteAddress(
    int? Position,
    string Type,
    string? PoBox,
    string? Extended,
    string? Street,
    string? Locality,
    string? Region,
    string? PostalCode,
    string? Country);

/// <summary>
/// A validated, normalised contact on its way to the store: names trimmed and nulled when blank,
/// child lines non-blank and in the order they must be stored. Only
/// <see cref="Services.ContactValidator"/> produces one, so the store never re-checks the rules.
/// </summary>
public sealed record ContactWrite(
    string? FirstName,
    string? LastName,
    string? Nickname,
    string? DisplayName,
    string? MiddleName,
    string? NamePrefix,
    string? NameSuffix,
    string? Organization,
    string? Department,
    string? JobTitle,
    string? Birthday,
    string? Website,
    string? Notes,
    bool IsFavorite,
    IReadOnlyList<ContactWriteEmail> Addresses,
    IReadOnlyList<ContactWritePhone> Phones,
    IReadOnlyList<ContactWriteAddress> PostalAddresses,
    string Source);
