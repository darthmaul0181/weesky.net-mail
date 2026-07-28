namespace weesky.Snoopy.Microservice.Models.Contacts;

public sealed record ContactImportError(int Line, string Reason);

/// <summary>What the store did. Its error list is unbounded — the controller caps it.</summary>
public sealed record ContactImportOutcome(
    int Created, int Merged, int Skipped, int Failed, IReadOnlyList<ContactImportError> Errors);

/// <summary>
/// What the client reads. The four counters count rows and add up to the file's data rows;
/// <paramref name="TotalErrors"/> counts every reason, including the ones past the cap on
/// <paramref name="Errors"/> — a wholly bad file must not answer ten thousand messages.
/// </summary>
public sealed record ContactImportReport(
    int Created, int Merged, int Skipped, int Failed, int TotalErrors,
    IReadOnlyList<ContactImportError> Errors);
