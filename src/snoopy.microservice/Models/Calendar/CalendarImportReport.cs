using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// What POST /api/Calendars/{id}/Import answers. <paramref name="TotalErrors"/> counts every
/// resource that failed, including the ones past the cap on <paramref name="Errors"/> — a wholly
/// bad file must not answer thousands of messages, mirroring <see cref="ContactImportReport"/>.
/// </summary>
public sealed record CalendarImportReport(
    int Created, int Replaced, int IgnoredTodos, int IgnoredJournals, int Failed, int TotalErrors,
    IReadOnlyList<ContactImportError> Errors);
