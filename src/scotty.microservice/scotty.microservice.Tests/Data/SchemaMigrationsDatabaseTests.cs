using DbUp.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MySqlConnector;
using weesky.Scotty.Microservice.Data;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Data;

[Collection(nameof(MariaDbCollection))]
public sealed class SchemaMigrationsDatabaseTests(MariaDbFixture db)
{
    private static readonly IReadOnlyList<SqlScript> TwoScripts =
    [
        SchemaMigrations.Embedded[0],
        new("0002_probe.sql", "-- a comment; with $dollar$ and a semicolon\nCREATE TABLE probe (id int NOT NULL);"),
    ];

    [SkippableFact]
    public async Task Apply_CreatesTheWholeSchemaOnAnEmptyDatabase()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        Assert.Equal(["0001_initial.sql"], SchemaMigrations.Apply(connection));

        Assert.Equal(27 + 1, await CountTablesAsync(connection));   // + schema_migrations
        Assert.Empty(SchemaMigrations.Pending(connection));
    }

    [SkippableFact]
    public async Task Apply_TwiceAppliesNothingTheSecondTime()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        SchemaMigrations.Apply(connection);

        Assert.Empty(SchemaMigrations.Apply(connection));
    }

    [SkippableFact]
    public async Task Apply_RunsScriptsHoldingSemicolonsAndDollarsAsWritten()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        Assert.Equal(["0001_initial.sql", "0002_probe.sql"], SchemaMigrations.Apply(connection, TwoScripts));
        Assert.Equal(27 + 2, await CountTablesAsync(connection));
    }

    [SkippableFact]
    public async Task Pending_NamesEveryScriptUnderAReadOnlyAccountWithoutWritingAnything()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var root = await db.NewDatabaseAsync();
        var readOnly = await db.NewReadOnlyUserAsync(new MySqlConnectionStringBuilder(root).Database);

        Assert.Equal(["0001_initial.sql"], SchemaMigrations.Pending(readOnly));
        Assert.Equal(0, await CountTablesAsync(root));
    }

    /// <summary>
    /// The owner's databases were created by the old install.sql and are adopted by hand: the journal
    /// table and its one row are written by the bootstrap script, then nothing must run again.
    /// </summary>
    [SkippableFact]
    public async Task Pending_TrustsAJournalWrittenByTheBootstrapScript()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        await db.ExecuteAsync(SchemaMigrations.Embedded[0].Contents, connection);
        await db.ExecuteAsync(await File.ReadAllTextAsync(BootstrapJournalPath()), connection);

        Assert.Empty(SchemaMigrations.Pending(connection));
        Assert.Empty(SchemaMigrations.Apply(connection));
    }

    [SkippableFact]
    public async Task Apply_DoesNotJournalAFailedScriptAndNamesIt()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        IReadOnlyList<SqlScript> broken = [SchemaMigrations.Embedded[0], new("0002_broken.sql", "CREATE TABLE ;")];

        var error = Assert.Throws<InvalidOperationException>(() => SchemaMigrations.Apply(connection, broken));

        Assert.Contains("0002_broken.sql", error.Message);
        Assert.Equal(["0002_broken.sql"], SchemaMigrations.Pending(connection, broken));
    }

    [SkippableFact]
    public async Task Apply_RunsEachScriptOnceWhenTwoStartsMigrateTogether()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        var applied = await Task.WhenAll(
            Task.Run(() => SchemaMigrations.Apply(connection)),
            Task.Run(() => SchemaMigrations.Apply(connection)));

        Assert.Equal(["0001_initial.sql"], applied.SelectMany(names => names));
    }

    [SkippableFact]
    public async Task TheSchemaHoldsEveryTableAndColumnTheModelMaps()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        SchemaMigrations.Apply(connection);
        var columns = await ColumnsAsync(connection);

        using var context = new PreferencesDbContext(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseMySql(connection, ServerVersion.AutoDetect(connection)).Options);
        var missing = context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .SelectMany(entity => entity.GetProperties().Select(property =>
            {
                var table = entity.GetTableName()!;
                return $"{table}.{property.GetColumnName(StoreObjectIdentifier.Table(table, entity.GetSchema()))}";
            }))
            .Where(column => !columns.Contains(column))
            .ToList();

        Assert.Empty(missing);
    }

    private static string BootstrapJournalPath() =>
        Path.Combine(AppContext.BaseDirectory, "Data", "schema-migrations-bootstrap-journal.sql");

    private static async Task<int> CountTablesAsync(string connection) =>
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()"));

    private static async Task<HashSet<string>> ColumnsAsync(string connection)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        await using var reader = await new MySqlCommand(
            "SELECT CONCAT(table_name, '.', column_name) FROM information_schema.columns WHERE table_schema = DATABASE()",
            c).ExecuteReaderAsync();
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) set.Add(reader.GetString(0));
        return set;
    }

    private static async Task<object?> ScalarAsync(string connection, string sql)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        return await new MySqlCommand(sql, c).ExecuteScalarAsync();
    }
}
