using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class SieveConnectionTests
{
    // A record's synthesised ToString prints every member, so one {Connection} in a log template
    // would dump a live master password.
    [Fact]
    public void ToString_NeverPrintsThePassword()
    {
        var connection = new SieveConnection("sieve.home.test", 4190, "alice@weesky.be", "master", "master-secret");

        var text = connection.ToString();

        Assert.DoesNotContain("master-secret", text, StringComparison.Ordinal);
        Assert.Contains("sieve.home.test", text, StringComparison.Ordinal);
    }
}
