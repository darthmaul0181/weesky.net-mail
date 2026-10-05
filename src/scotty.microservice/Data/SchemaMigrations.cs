using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        try
        {
            lockConnection.Open();
            TakeLock(lockConnection);
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
                ReleaseLock(lockConnection);
            }
        }
        finally
        {
            // The schema account's sessions would otherwise idle in the pool beside the service's.
            MySqlConnection.ClearPool(lockConnection);
        }
    }

    public static void EnsureCurrent(IConfiguration configuration, ILogger logger)
    {
        var schema = configuration.GetConnectionString("WebmailSchema");
        if (!string.IsNullOrWhiteSpace(schema))
        {
            foreach (var name in Apply(schema)) logger.LogInformation("Schema migration {Script} applied", name);
            return;
        }

        var pending = Pending(configuration.GetConnectionString("WebmailPreferencesDatabase")!);
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"Schema is behind: {string.Join(", ", pending)} not applied. " +
                "Run `scotty.microservice migrate`, or set ConnectionStrings__WebmailSchema.");
    }

    // `sudo` empties the environment and a command line shows in `ps`: standard input carries it then.
    public static int RunCommand(string? fromEnvironment, TextReader input, TextWriter output, TextWriter error)
    {
        var connection = string.IsNullOrWhiteSpace(fromEnvironment) ? input.ReadLine()?.Trim() : fromEnvironment;
        if (string.IsNullOrEmpty(connection))
        {
            error.WriteLine("No schema connection: set ConnectionStrings__WebmailSchema, or pass it on standard input.");
            return 1;
        }

        try
        {
            var applied = Apply(connection);
            foreach (var name in applied) output.WriteLine($"Schema migration {name} applied");
            if (applied.Count == 0) output.WriteLine("Schema is current.");
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or MySqlException or ArgumentException)
        {
            error.WriteLine(e.Message);
            return 1;
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

    private static void TakeLock(MySqlConnection connection)
    {
        switch (Scalar(connection, $"SELECT GET_LOCK('{LockName}', {LockTimeoutSeconds})"))
        {
            case 1 or 1L:
                return;
            case 0 or 0L:
                throw new InvalidOperationException(
                    $"Could not take the schema lock within {LockTimeoutSeconds} s: another start is migrating this database.");
            default:
                throw new InvalidOperationException("Could not take the schema lock: the database refused it.");
        }
    }

    // A lost connection has released the lock with it, and the upgrade's own error is the one to report.
    private static void ReleaseLock(MySqlConnection connection)
    {
        try
        {
            Scalar(connection, $"SELECT RELEASE_LOCK('{LockName}')");
        }
        catch (MySqlException)
        {
        }
    }

    private static object? Scalar(MySqlConnection connection, string sql) =>
        new MySqlCommand(sql, connection).ExecuteScalar();

    private static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
}
