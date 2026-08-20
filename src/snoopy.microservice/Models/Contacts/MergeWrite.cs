namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// An import merge's write: only the fields the merge actually filled (non-null), plus the
/// addresses it appended. A merge never overwrites what the contact already had.
/// </summary>
public sealed record MergeWrite(
    string? FirstName, string? LastName, string? Nickname, IReadOnlyList<string> AddedAddresses);
