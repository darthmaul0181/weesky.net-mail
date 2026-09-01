using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>The card's two species. Pinned strings: the MariaDB ENUM refuses anything else.</summary>
public static class ContactKinds
{
    public const string Individual = "individual";
    public const string Group = "group";
}

/// <summary>
/// The kind clause, written once — ContactVisibility's twin sister. Every product read filters
/// through one of these; the DAV side filters through NEITHER: the collection serves both
/// species, and that is what makes it conform (décision 4). "GroupCards", not "Groups": that
/// word already names the vCard property group in this code.
/// </summary>
internal static class ContactKindQueries
{
    internal static IQueryable<Contact> Individuals(this IQueryable<Contact> contacts) =>
        contacts.Where(c => c.Kind == ContactKinds.Individual);

    internal static IQueryable<Contact> GroupCards(this IQueryable<Contact> contacts) =>
        contacts.Where(c => c.Kind == ContactKinds.Group);
}
