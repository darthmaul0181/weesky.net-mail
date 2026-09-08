using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Models.Dav;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// Everything the property set may need, gathered once rather than fetched per property: a
/// <c>PROPFIND</c> over a full book asks the same questions 5000 times, and a factory reaching for
/// the database would turn one query into one per card per property. Every member past the five
/// of 4c is LAST and defaulted, so the constructions of 4c keep their shape.
/// </summary>
/// <param name="Kind">Which table answers.</param>
/// <param name="UserId">Whose collection — every href of the answer is cut from it.</param>
/// <param name="PrincipalAddress">The principal's <c>displayname</c>: the user's own address.</param>
/// <param name="Card">The card, on <see cref="DavResourceKind.Card"/> alone; null elsewhere.</param>
/// <param name="State">The collection's sync state, or null for one that has never emitted any.</param>
/// <param name="CollectionName">The calendar's URL segment, decoded once; null off the calendar tree.</param>
/// <param name="Addresses">Every address the principal answers to; the controller poses the list,
/// the table reads <c>Addresses ?? []</c>.</param>
/// <param name="Calendar">The calendar, on a calendar or an event; null elsewhere.</param>
/// <param name="Event">The event, on an event alone; null elsewhere.</param>
/// <param name="CardDavEnabled">Whether the address-book home is announced on the principal.</param>
/// <param name="CalDavEnabled">Whether the calendar home is announced on the principal.</param>
internal sealed record DavResourceContext(
    DavResourceKind Kind, Guid UserId, string PrincipalAddress, DavCard? Card, SyncState? State,
    string? CollectionName = null, IReadOnlyList<string>? Addresses = null,
    DavCalendar? Calendar = null, DavEvent? Event = null,
    bool CardDavEnabled = true, bool CalDavEnabled = true);
