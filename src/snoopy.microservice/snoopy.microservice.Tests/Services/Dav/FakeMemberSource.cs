using System.Runtime.CompilerServices;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Tests.Services.Dav;

/// <summary>
/// A collection held in memory, where a member IS its name and its rank is a table entry: hrefs
/// are <c>/m/{name}</c>, anything else is not one of ours, and the only property served is a
/// <c>getetag</c> equal to the quoted name.
/// </summary>
internal sealed class FakeMemberSource(
    IReadOnlyDictionary<string, ulong>? members = null, IReadOnlyList<DavTombstone>? tombstones = null)
    : IDavMemberSource<string>
{
    private const string Prefix = "/m/";

    private readonly IReadOnlyDictionary<string, ulong> ranks =
        members ?? new Dictionary<string, ulong>(StringComparer.Ordinal);

    private readonly IReadOnlyList<DavTombstone> tombstones = tombstones ?? [];

    internal List<IReadOnlyList<string>> FindManyCalls { get; } = [];

    internal List<(ulong After, ulong UpTo)> TombstoneCalls { get; } = [];

    /// <summary>Replaces <see cref="Prepare"/> — the seam for a source that refuses.</summary>
    internal Func<XDocument, (DavPropertyRequest, IDavMemberResolver<string>)>? OnPrepare { get; init; }

    public Task<IReadOnlyList<string>> FindManyAsync(IReadOnlyList<string> davNames, CancellationToken ct)
    {
        FindManyCalls.Add(davNames);
        return Task.FromResult<IReadOnlyList<string>>([.. davNames.Where(ranks.ContainsKey)]);
    }

    public async IAsyncEnumerable<string> ChangedAsync(ulong after, ulong upTo,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var member in ranks.Where(r => r.Value > after && r.Value <= upTo)
                     .OrderBy(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal))
            yield return member.Key;
    }

    public Task<IReadOnlyList<DavTombstone>> TombstonesAsync(ulong after, ulong upTo, CancellationToken ct)
    {
        TombstoneCalls.Add((after, upTo));
        return Task.FromResult<IReadOnlyList<DavTombstone>>(
            [.. tombstones.Where(t => t.Rank > after && t.Rank <= upTo).OrderBy(t => t.Rank)]);
    }

    public string? MemberNameOf(string href) =>
        href.StartsWith(Prefix, StringComparison.Ordinal) ? href[Prefix.Length..] : null;

    public string HrefOf(string davName) => Prefix + davName;

    public string DavNameOf(string member) => member;

    public ulong RankOf(string member) => ranks[member];

    public (DavPropertyRequest Request, IDavMemberResolver<string> Resolver) Prepare(XDocument body) =>
        OnPrepare?.Invoke(body) ?? (DavPropertyRequest.Parse(body), new Resolver());

    private sealed class Resolver : IDavMemberResolver<string>
    {
        public (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, string member)
        {
            List<XElement> found = [];
            List<XName> missing = [];
            foreach (var name in request.Names)
            {
                if (name == DavXml.Dav + "getetag") found.Add(new XElement(name, $"\"{member}\""));
                else missing.Add(name);
            }

            return (found, missing);
        }
    }
}
