using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.Data;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Data;

[Collection(nameof(MariaDbCollection))]
public sealed class SchemaMigrationsEntryPointsTests(MariaDbFixture db)
{
    private static IConfiguration Settings(string service, string? schema = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebmailPreferencesDatabase"] = service,
            ["ConnectionStrings:WebmailSchema"] = schema,
        }).Build();

    [SkippableFact]
    public async Task EnsureCurrent_RefusesABehindSchemaWithoutASchemaConnection()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        var error = Assert.Throws<InvalidOperationException>(
            () => SchemaMigrations.EnsureCurrent(Settings(connection), NullLogger.Instance));

        Assert.Equal(
            "Schema is behind: 0001_initial.sql not applied. Run `scotty.microservice migrate`, " +
            "or set ConnectionStrings__WebmailSchema.", error.Message);
    }

    [SkippableFact]
    public async Task EnsureCurrent_MigratesWhenASchemaConnectionIsSet()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        SchemaMigrations.EnsureCurrent(Settings(connection, schema: connection), NullLogger.Instance);

        Assert.Empty(SchemaMigrations.Pending(connection));
    }

    [SkippableFact]
    public async Task EnsureCurrent_StartsOnACurrentSchema()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        SchemaMigrations.Apply(connection);

        SchemaMigrations.EnsureCurrent(Settings(connection), NullLogger.Instance);
    }

    [SkippableFact]
    public async Task RunCommand_ReadsTheConnectionFromStandardInputAndExitsZero()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        var output = new StringWriter();

        var code = SchemaMigrations.RunCommand(null, new StringReader(connection + "\n"), output, TextWriter.Null);

        Assert.Equal(0, code);
        Assert.Contains("0001_initial.sql", output.ToString());
        Assert.Empty(SchemaMigrations.Pending(connection));
    }

    [Fact]
    public void RunCommand_ExitsOneWithoutAConnection()
    {
        var error = new StringWriter();

        Assert.Equal(1, SchemaMigrations.RunCommand(null, TextReader.Null, TextWriter.Null, error));
        Assert.Contains("ConnectionStrings__WebmailSchema", error.ToString());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("Server=127.0.0.1;Bogus=1")]
    public void RunCommand_ExitsOneWithAReadableMessageOnAMalformedConnection(string connection)
    {
        var error = new StringWriter();

        Assert.Equal(1, SchemaMigrations.RunCommand(connection, TextReader.Null, TextWriter.Null, error));
        Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain("   at ", error.ToString());
    }

    [Fact]
    public void RunCommand_ExitsOneWithAReadableMessageOnAnUnreachableDatabase()
    {
        var error = new StringWriter();

        var code = SchemaMigrations.RunCommand(
            "Server=127.0.0.1;Port=1;Database=x;User=x;Password=x;Connection Timeout=2",
            TextReader.Null, TextWriter.Null, error);

        Assert.Equal(1, code);
        Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain("   at ", error.ToString());
    }
}
