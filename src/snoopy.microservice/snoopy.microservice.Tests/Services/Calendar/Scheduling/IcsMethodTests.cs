using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Scheduling;

public class IcsMethodTests
{
    [Fact]
    public void With_InsertsMethodAfterVersion_OrReplacesAnExistingOne_KeepingEveryOtherByte()
    {
        var stored = InvitationParserTests.Fixture("webmail-invited");
        var request = IcsMethod.With(stored, "REQUEST");

        var lines = request.Split("\r\n");
        Assert.Equal("VERSION:2.0", lines[2]);
        Assert.Equal("METHOD:REQUEST", lines[3]);
        Assert.Equal(stored.Replace("VERSION:2.0\r\n", "VERSION:2.0\r\nMETHOD:REQUEST\r\n"), request);

        var cancelled = IcsMethod.With(InvitationParserTests.Fixture("thunderbird-request"), "CANCEL");
        Assert.Single(cancelled.Split("\r\n"), l => l.StartsWith("METHOD:", StringComparison.Ordinal));
        Assert.Contains("METHOD:CANCEL\r\n", cancelled);
    }
}
