using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class FullNameChangeTests
{
    [Fact]
    public void FullName_CanBeSetAndRead()
    {
        var change = new FullNameChange { FullName = "John Doe" };
        Assert.Equal("John Doe", change.FullName);
    }
}
