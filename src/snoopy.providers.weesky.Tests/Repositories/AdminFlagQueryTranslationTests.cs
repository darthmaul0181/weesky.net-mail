using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Providers.Weesky.Data;
using weesky.Snoopy.Providers.Weesky.Repositories;
using Xunit;

namespace weesky.Snoopy.Providers.Weesky.Tests.Repositories;

/// <summary>
/// The admin flag decides authorization, and <c>domains.name</c> has no unique constraint, so which
/// rows the statement may read is a security property. EF InMemory answers both shapes identically
/// — only the relational translation shows whether the users are restricted to one domain row — so
/// this reads the SQL the real provider emits. It needs no server: nothing is executed.
/// </summary>
public sealed class AdminFlagQueryTranslationTests
{
    private static ApplicationDbContext MySqlContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql("server=unused;database=dovecot;user=u;password=p", ServerVersion.Parse("10.11.0-mariadb"),
                mySql => mySql.EnableStringComparisonTranslations())
            .Options);

    [Fact]
    public void AdminFlagQuery_RestrictsTheUserToOneDomainRow()
    {
        using var ctx = MySqlContext();

        var sql = AdminRepository.AdminFlagQuery(ctx, "alice", "weesky.be").ToQueryString();

        // One domain row, chosen by the subquery, then the user inside it. Joining `domains`
        // instead would let a namesake in a second row bearing the same name answer.
        Assert.Contains("FROM `domains`", sql);
        Assert.Contains("LIMIT 1", sql);
        Assert.DoesNotContain("JOIN", sql);
    }
}
