using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>What multiget and sync-collection need of a collection, whichever protocol owns it.
/// One instance is bound to one collection (a user's book, a calendar) for one request.</summary>
internal interface IDavMemberSource<TMember>
{
    Task<IReadOnlyList<TMember>> FindManyAsync(IReadOnlyList<string> davNames, CancellationToken ct);

    IAsyncEnumerable<TMember> ChangedAsync(ulong after, ulong upTo, CancellationToken ct);

    Task<IReadOnlyList<DavTombstone>> TombstonesAsync(ulong after, ulong upTo, CancellationToken ct);

    /// <summary>The resource an href from a body designates, or null when it is not a member of
    /// THIS collection — a card href on a calendar, another user's, an invalid name.</summary>
    string? MemberNameOf(string href);

    string HrefOf(string davName);

    string DavNameOf(TMember member);

    ulong RankOf(TMember member);

    /// <summary>Reads the report body ONCE — the properties asked, and whether address-data /
    /// calendar-data is among them and under which form. MAY refuse. Called before the multistatus
    /// is opened, never per member.</summary>
    (DavPropertyRequest Request, IDavMemberResolver<TMember> Resolver) Prepare(XDocument body);
}
