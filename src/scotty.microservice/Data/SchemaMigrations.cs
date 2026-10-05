using System.Reflection;
using DbUp;
using DbUp.Engine;
using MySqlConnector;

namespace weesky.Scotty.Microservice.Data;

/// <summary>
/// The scotty_webmail schema as numbered SQL scripts (Data/Migrations), applied by DbUp and journalled
/// in <see cref="JournalTable"/> under their short name.
/// </summary>
internal static class SchemaMigrations
{
    public const string JournalTable = "schema_migrations";
    private const string ResourceMarker = ".Migrations.";
    private const string LockName = "scotty_webmail_schema";
    private const int LockTimeoutSeconds = 60;

    public static IReadOnlyList<SqlScript> Embedded { get; } = Load(typeof(SchemaMigrations).Assembly);

    public static IReadOnlyList<string> Pending(string connectionString, IReadOnlyList<SqlScript>? scripts = null) =>
        [.. Engine(connectionString, scripts ?? Embedded).GetScriptsToExecute().Select(script => script.Name)];

    // The lock is held on a connection of its own for the whole upgrade: a second start waits, then
    // finds nothing left to do. It dies with the connection if the process does.
    public static IReadOnlyList<string> Apply(string schemaConnectionString, IReadOnlyList<SqlScript>? scripts = null)
    {
        using var lockConnection = new MySqlConnection(schemaConnectionString);
        lockConnection.Open();
        if (Convert.ToInt64(Scalar(lockConnection, $"SELECT GET_LOCK('{LockName}', {LockTimeoutSeconds})") ?? 0L) != 1)
            throw new InvalidOperationException(
                $"Could not take the schema lock within {LockTimeoutSeconds} s: another start is migrating this database.");

        try
        {
            var result = Engine(schemaConnectionString, scripts ?? Embedded).PerformUpgrade();
            if (!result.Successful)
                throw new InvalidOperationException(
                    $"Schema migration {result.ErrorScript?.Name} failed: {result.Error.Message}", result.Error);
            return [.. result.Scripts.Select(script => script.Name)];
        }
        finally
        {
            Scalar(lockConnection, $"SELECT RELEASE_LOCK('{LockName}')");
        }
    }

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

    private static UpgradeEngine Engine(string connectionString, IReadOnlyList<SqlScript> scripts) =>
        DeployChanges.To.MySqlDatabase(connectionString)
            .WithScripts(scripts)
            .JournalToMySqlTable(new MySqlConnectionStringBuilder(connectionString).Database, JournalTable)
            .WithVariablesDisabled()
            .LogToNowhere()
            .Build();

    private static object? Scalar(MySqlConnection connection, string sql) =>
        new MySqlCommand(sql, connection).ExecuteScalar();

    private static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
}
