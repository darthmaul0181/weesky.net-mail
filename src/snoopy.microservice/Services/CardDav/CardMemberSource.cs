using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>The one address book of a user, as the two generic reports read it.</summary>
internal sealed class CardMemberSource(IDavContactReader contacts, Guid userId, string principalAddress)
    : IDavMemberSource<DavCard>
{
    public Task<IReadOnlyList<DavCard>> FindManyAsync(IReadOnlyList<string> davNames, CancellationToken ct) =>
        contacts.FindManyAsync(userId, davNames, ct);

    public IAsyncEnumerable<DavCard> ChangedAsync(ulong after, ulong upTo, CancellationToken ct) =>
        contacts.ChangedAsync(userId, after, upTo, ct);

    public async Task<IReadOnlyList<DavTombstone>> TombstonesAsync(ulong after, ulong upTo, CancellationToken ct) =>
        [.. (await contacts.TombstonesAsync(userId, after, upTo, ct))
            .Select(tombstone => new DavTombstone(tombstone.DavName, tombstone.SyncSequence))];

    public string? MemberNameOf(string href) =>
        DavPaths.Parse(href) is { Kind: DavResourceKind.Card } resource && resource.UserId == userId
        && DavName.IsValid(resource.DavName)
            ? resource.DavName
            : null;

    public string HrefOf(string davName) => DavPaths.Card(userId, davName);

    public string DavNameOf(DavCard member) => member.DavName;

    public ulong RankOf(DavCard member) => member.SyncSequence;

    public (DavPropertyRequest Request, IDavMemberResolver<DavCard> Resolver) Prepare(XDocument body)
    {
        // address-data is deliberately served on a multiget alone: RFC 6352 § 10.4 defines it only
        // in query and multiget, and on a sync-collection the tables leave it in the 404 propstat,
        // where Thunderbird reads the absence and chains a multiget.
        if (ReportRequest.KindOf(body) is not DavReportKind.Multiget)
            return (DavPropertyRequest.Parse(body), new Resolver(userId, principalAddress, null));

        var request = AddressDataFilter.PropertiesAsked(body);
        var addressData = AddressDataFilter.Asked(body); // may refuse — before anything is written
        return (request, new Resolver(userId, principalAddress, addressData));
    }

    private sealed class Resolver(Guid userId, string principalAddress, AddressDataRequest? addressData)
        : IDavMemberResolver<DavCard>
    {
        public (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, DavCard member)
        {
            var context = new DavResourceContext(
                DavResourceKind.Card, userId, principalAddress, member, null);
            var (found, missing) = CardDavProperties.Resolve(request, context);
            if (addressData is not null)
                found.Add(AddressDataFilter.Element(member.VCardRaw, addressData));
            return (found, missing);
        }
    }
}
