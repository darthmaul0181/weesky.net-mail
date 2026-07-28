namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One row on its way into the book. Free of any notion of CSV, so the vCard import of the next
/// slice feeds the same merge rather than a second one. <c>Line</c> is the line in the source
/// file, header included — what the user reads.
/// </summary>
public sealed record ContactImportRow(
    int Line,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    string? VCard);
