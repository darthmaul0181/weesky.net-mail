namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>One read-side e-mail line. <c>Params</c> and <c>GroupName</c> are display-only: they never enter a write.</summary>
public sealed record ContactDetailEmail(int Position, string Address, string Type, int Pref, string Params, string GroupName);

/// <summary>One read-side phone line.</summary>
public sealed record ContactDetailPhone(int Position, string Number, string Type, int Pref, string Params, string GroupName);

/// <summary>One read-side postal address line.</summary>
public sealed record ContactDetailAddress(
    int Position,
    string Type,
    int Pref,
    string Params,
    string GroupName,
    string? PoBox,
    string? Extended,
    string? Street,
    string? Locality,
    string? Region,
    string? PostalCode,
    string? Country);

/// <summary>
/// The full contact card, as answered by GET /api/Contacts/{id}. Every child line carries its
/// <c>Position</c> — the handle a PUT must return — plus <c>Type</c>, <c>Pref</c>, <c>Params</c>
/// and <c>GroupName</c> for display; the last two never travel back on a write. <c>CardHash</c> is
/// what a PUT must send back on <see cref="ContactRequest.CardHash"/> to prove it read this very
/// version — the same check as CardDAV's <c>If-Match</c>, in the webmail's own language.
/// </summary>
public sealed record ContactDetail(
    Guid Id,
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
    bool HasPhoto,
    IReadOnlyList<ContactDetailEmail> Addresses,
    IReadOnlyList<ContactDetailPhone> Phones,
    IReadOnlyList<ContactDetailAddress> PostalAddresses,
    string CardHash);
