using System.Net;

namespace weesky.Scotty.Microservice.Services.Bimi;

internal static class BimiRecord
{
    public static Uri? LogoOf(IEnumerable<string> txtRecords)
    {
        var tags = txtRecords.Select(Tags).FirstOrDefault(IsBimi);
        var logos = tags?.Where(t => t.Name == "l").ToList();
        return logos is [var only] && Uri.TryCreate(only.Value, UriKind.Absolute, out var uri) && IsFetchable(uri) ? uri : null;
    }

    public static bool Exists(IEnumerable<string> txtRecords) => txtRecords.Select(Tags).Any(IsBimi);

    public static bool IsFetchable(Uri uri) =>
        uri is { Scheme: "https", Port: 443, UserInfo: "", HostNameType: UriHostNameType.Dns };

    private static bool IsBimi(List<(string Name, string Value)> tags) =>
        tags is [var first, ..] && first.Name == "v" && first.Value.Equals("BIMI1", StringComparison.OrdinalIgnoreCase);

    private static List<(string Name, string Value)> Tags(string record) =>
        record.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2)
            .Select(p => (p[0].ToLowerInvariant(), p[1]))
            .ToList();
}
