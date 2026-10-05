using System.Reflection;
using DbUp.Engine;

namespace weesky.Scotty.Microservice.Data;

/// <summary>
/// The scotty_webmail schema as numbered SQL scripts (Data/Migrations), applied by DbUp and journalled
/// in <see cref="JournalTable"/> under their short name.
/// </summary>
internal static class SchemaMigrations
{
    public const string JournalTable = "schema_migrations";
    private const string ResourceMarker = ".Migrations.";

    public static IReadOnlyList<SqlScript> Embedded { get; } = Load(typeof(SchemaMigrations).Assembly);

    internal static IReadOnlyList<SqlScript> Load(Assembly assembly) =>
    [
        .. assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => new SqlScript(
                name[(name.LastIndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..],
                Read(assembly, name)))
            .OrderBy(script => script.Name, StringComparer.Ordinal),
    ];

    private static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
}
