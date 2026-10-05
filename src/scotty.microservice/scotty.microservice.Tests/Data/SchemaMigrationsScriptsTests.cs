using System.Text.RegularExpressions;
using weesky.Scotty.Microservice.Data;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Data;

/// <summary>A shipped script is never edited and never renumbered: the journal knows it by name.</summary>
public sealed class SchemaMigrationsScriptsTests
{
    [Fact]
    public void Embedded_StartsWithTheInitialSchema()
    {
        var first = SchemaMigrations.Embedded[0];

        Assert.Equal("0001_initial.sql", first.Name);
        Assert.Equal(27, Regex.Matches(first.Contents, @"^CREATE TABLE", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void Embedded_AreNumberedFromOneWithoutGapOrDuplicate()
    {
        var names = SchemaMigrations.Embedded.Select(s => s.Name).ToList();

        Assert.All(names, name => Assert.Matches(@"^\d{4}_[a-z0-9_]+\.sql$", name));
        Assert.Equal(Enumerable.Range(1, names.Count), names.Select(n => int.Parse(n[..4])));
    }

    [Fact]
    public void Embedded_NeverNameTheDatabase()
    {
        Assert.All(SchemaMigrations.Embedded, script =>
            Assert.DoesNotMatch(@"(?im)^\s*USE\s|`?scotty_webmail`?\.", script.Contents));
    }
}
