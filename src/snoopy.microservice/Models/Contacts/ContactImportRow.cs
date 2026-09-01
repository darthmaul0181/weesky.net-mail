namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One row on its way into the book. Free of any notion of CSV, so a vCard file feeds the same
/// merge rather than a second one. <c>Line</c> is the line in the source file, header included —
/// what the user reads. A row carries <b>either</b> the card a file brought — <c>VCard</c>, filed
/// verbatim, with the <c>Uid</c> it declares as the merge key tried before the address and the
/// name — <b>or</b> <c>Write</c>, the columns to compose one from. Never a UID of our making: the
/// identity a client synchronises on is the card's or the store's, never a reader's.
/// <c>IsGroup</c> is the card's own KIND, and it changes how the row resolves: a group is matched
/// on its UID alone, never on a name or an address (décision 19). A CSV never sets it.
/// </summary>
public sealed record ContactImportRow(
    int Line,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    string? VCard,
    string? Uid,
    ContactWrite? Write = null,
    bool IsGroup = false);
