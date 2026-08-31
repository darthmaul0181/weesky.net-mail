namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// An import merge's write: only the fields the merge actually filled (non-null), plus the
/// addresses it appended. A merge never overwrites what the contact already had, so a field the
/// target holds arrives null here and the card keeps its own. The two families are all-or-nothing:
/// a target holding one phone is handed none, since two spellings of the same number cannot be
/// told apart without a normalisation neither TEL nor ADR has.
/// </summary>
public sealed record MergeWrite(
    string? FirstName,
    string? LastName,
    string? Nickname,
    IReadOnlyList<string> AddedAddresses,
    string? MiddleName = null,
    string? NamePrefix = null,
    string? NameSuffix = null,
    string? DisplayName = null,
    string? Organization = null,
    string? Department = null,
    string? JobTitle = null,
    string? Birthday = null,
    string? Website = null,
    string? Notes = null,
    IReadOnlyList<ContactWritePhone>? Phones = null,
    IReadOnlyList<ContactWriteAddress>? PostalAddresses = null)
{
    /// <summary>Whether anything here reaches the card — what tells a merge it changed nothing.</summary>
    public bool IsEmpty =>
        FirstName == null && LastName == null && Nickname == null && AddedAddresses.Count == 0
        && MiddleName == null && NamePrefix == null && NameSuffix == null && DisplayName == null
        && Organization == null && Department == null && JobTitle == null && Birthday == null
        && Website == null && Notes == null && Phones == null && PostalAddresses == null;
}
