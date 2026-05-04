using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public class FullNameChangeTests
{
    [Fact]
    public void FullName_CanBeSetAndRead()
    {
        var change = new FullNameChange { FullName = "John Doe" };
        Assert.Equal("John Doe", change.FullName);
    }
}
