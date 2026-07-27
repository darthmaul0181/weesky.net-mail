namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// A validated, normalised contact on its way to the store: names trimmed and nulled when blank,
/// addresses non-blank and in the order they must be stored. Only
/// <see cref="Services.ContactValidator"/> produces one, so the store never re-checks the rules.
/// </summary>
public sealed record ContactWrite(
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    string Source);
