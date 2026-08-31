using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The protocol's visibility clause, written once: a row the 4a backfill has not reached — no
/// card, no hash — was never served, and every DAV read or delete must see the same absence.
/// </summary>
internal static class ContactVisibility
{
    internal static IQueryable<Contact> Visible(this IQueryable<Contact> contacts, Guid userId) =>
        contacts.Where(c =>
            c.UserId == userId && c.DavName != null && c.VCardRaw != null && c.CardHash != "");
}
