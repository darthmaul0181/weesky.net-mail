using MySqlConnector;
using Testcontainers.MariaDb;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>
/// One MariaDB per test run, a fresh database per test. Without Docker the tests are skipped on a
/// workstation and fail on CI, where a skipped schema test would be a silent hole.
/// </summary>
public sealed class MariaDbFixture : IAsyncLifetime
{
    private readonly MariaDbContainer container = new MariaDbBuilder().WithImage("mariadb:11.4").Build();

    public bool Unavailable { get; private set; }
    public string RootConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        try
        {
            await container.StartAsync();
        }
        catch (Exception) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            Unavailable = true;
            return;
        }

        RootConnectionString = new MySqlConnectionStringBuilder(container.GetConnectionString())
        {
            UserID = "root",
            Database = "",
        }.ConnectionString;
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public async Task<string> NewDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin");
        return new MySqlConnectionStringBuilder(RootConnectionString) { Database = name }.ConnectionString;
    }

    public async Task<string> NewReadOnlyUserAsync(string database)
    {
        var user = $"r{Guid.NewGuid():N}"[..16];
        await ExecuteAsync($"CREATE USER '{user}'@'%' IDENTIFIED BY 'readonly'");
        await ExecuteAsync($"GRANT SELECT ON `{database}`.* TO '{user}'@'%'");
        return new MySqlConnectionStringBuilder(RootConnectionString)
        {
            UserID = user,
            Password = "readonly",
            Database = database,
        }.ConnectionString;
    }

    public async Task ExecuteAsync(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? RootConnectionString);
        await connection.OpenAsync();
        await new MySqlCommand(sql, connection).ExecuteNonQueryAsync();
    }
}
