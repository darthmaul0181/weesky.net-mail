using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Scotty.Microservice.Authentication.Models;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class SessionSigningKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scotty-state-{Guid.NewGuid():N}");
    private string KeyFile => Path.Combine(_directory, SessionSigningKey.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Resolve_UsesTheConfiguredKeyAndWritesNothing()
    {
        var configured = new string('k', 64);

        Assert.Equal((configured, (string?)null), SessionSigningKey.Resolve(configured, _directory));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public void Resolve_GeneratesSixtyFourRandomBytesWhenNoKeyIsConfigured()
    {
        var (key, generatedIn) = SessionSigningKey.Resolve("", _directory);

        Assert.Equal(KeyFile, generatedIn);
        Assert.Equal(64, Convert.FromBase64String(key).Length);
        Assert.Equal(key, File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Resolve_ReadsTheSameKeyBackOnTheNextStart()
    {
        var (first, _) = SessionSigningKey.Resolve(null, _directory);

        var (second, generatedIn) = SessionSigningKey.Resolve(null, _directory);

        Assert.Equal(first, second);
        Assert.Null(generatedIn);
    }

    [Fact]
    public void Resolve_NeverRewritesAnExistingFile()
    {
        Directory.CreateDirectory(_directory);
        var existing = new string('e', 48);
        File.WriteAllText(KeyFile, existing + "\n");

        Assert.Equal((existing, (string?)null), SessionSigningKey.Resolve(null, _directory));
        Assert.Equal(existing + "\n", File.ReadAllText(KeyFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Resolve_RefusesToStartOnAnUnusableFileAndLeavesItAlone(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(KeyFile, content);

        var error = Assert.Throws<InvalidOperationException>(() => SessionSigningKey.Resolve(null, _directory));

        Assert.Contains(KeyFile, error.Message);
        Assert.Equal(content, File.ReadAllText(KeyFile));
    }

    [Fact]
    public void Resolve_RefusesToStartOnAnUnreadableFile()
    {
        Directory.CreateDirectory(KeyFile);   // a directory where the file should be: unreadable on every OS

        var error = Assert.Throws<InvalidOperationException>(() => SessionSigningKey.Resolve(null, _directory));

        Assert.Contains(KeyFile, error.Message);
    }

    [Fact]
    public async Task Resolve_GivesConcurrentStartsOneKeyAndLeavesNoTemporaryFile()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => SessionSigningKey.Resolve(null, _directory))));

        Assert.Single(results.Select(r => r.Key).Distinct());
        Assert.Single(results, r => r.GeneratedIn is not null);
        Assert.Equal(new[] { KeyFile }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void Resolve_CreatesTheFileReadableByTheServiceUserOnly()
    {
        if (OperatingSystem.IsWindows()) return;   // Unix permissions; CI runs this on Linux

        SessionSigningKey.Resolve(null, _directory);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile));
    }

    [Fact]
    public void AGeneratedKeyPassesTheStartupValidation()
    {
        var (key, _) = SessionSigningKey.Resolve(null, _directory);

        Assert.Equal(key, Compose(configuredKey: "", resolvedKey: key).Value.Key);
    }

    [Fact]
    public void AShortConfiguredKeyIsStillRefused()
    {
        var (key, _) = SessionSigningKey.Resolve("short", _directory);

        Assert.Throws<OptionsValidationException>(() => Compose(configuredKey: "short", resolvedKey: key).Value);
    }

    // The composition Program.cs runs: bound from configuration, then the resolved key, then validated.
    private static IOptions<TokenConstants> Compose(string configuredKey, string resolvedKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenConstants:Key"] = configuredKey })
            .Build();
        var provider = new ServiceCollection()
            .AddScottyOptions(configuration)
            .PostConfigure<TokenConstants>(constants => constants.Key = resolvedKey)
            .BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TokenConstants>>();
    }
}
