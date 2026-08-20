namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// The backfill's write (task 8): only the columns that were already modelled before this slice —
/// names, nickname and the e-mail block. Everything else on the card is out of bounds.
/// </summary>
public sealed record ReconcileWrite(
    string? FirstName, string? LastName, string? Nickname, IReadOnlyList<string> Addresses);
