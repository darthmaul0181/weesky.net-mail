using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// What one imported file did. <c>Replaced</c> counts the UIDs the calendar already held and this
/// file overwrote whole; the todos and journals are counted rather than stored (fonctionnalité 6).
/// The errors reuse <see cref="ContactImportError"/>, whose <c>Line</c> here is the 1-based rank of
/// the resource in the split, not a line of the file: grouping by UID goes through the object
/// model, which loses the original line numbers.
/// </summary>
public sealed record CalendarImportOutcome(
    int Created, int Replaced, int IgnoredTodos, int IgnoredJournals, int Failed,
    IReadOnlyList<ContactImportError> Errors);
