using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// Everything the property set may need, gathered once rather than fetched per property: a
/// <c>PROPFIND</c> over a full book asks the same questions 5000 times, and a factory reaching for
/// the database would turn one query into one per card per property.
/// </summary>
/// <param name="Kind">Which of the five tables answers.</param>
/// <param name="UserId">Whose book — every href of the answer is cut from it.</param>
/// <param name="PrincipalAddress">The principal's <c>displayname</c>: the user's own address.</param>
/// <param name="Card">The card, on <see cref="DavResourceKind.Card"/> alone; null elsewhere.</param>
/// <param name="State">The book's sync state, or null for a book that has never emitted one.</param>
internal sealed record DavResourceContext(
    DavResourceKind Kind, Guid UserId, string PrincipalAddress, DavCard? Card, SyncState? State);
