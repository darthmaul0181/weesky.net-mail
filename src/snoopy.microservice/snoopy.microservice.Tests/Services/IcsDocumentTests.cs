using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IcsDocumentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an icalendar at all")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\n")]
    public void TryLoad_AnswersNullOnBothDoors(string ics) => Assert.Null(IcsDocument.TryLoad(ics));

    [Fact]
    public void TryLoad_AcceptsBareLineFeeds() =>
        Assert.NotNull(IcsDocument.TryLoad(Ics.Events(("a", null)).Replace("\r\n", "\n")));

    [Fact]
    public void Serialize_WritesCrLf() =>
        Assert.Contains("\r\n", IcsDocument.Serialize(IcsDocument.TryLoad(Ics.Events(("a", null)))!), StringComparison.Ordinal);

    [Fact]
    public void HashOf_IsLowercaseSha256Hex()
    {
        var hash = IcsDocument.HashOf("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void MasterOf_IgnoresOverrides()
    {
        Assert.Equal("rule", IcsDocument.MasterOf(IcsDocument.TryLoad(Ics.RuleWithOverride("FREQ=WEEKLY;COUNT=2", "20261130T090000"))!)!.Uid);
        Assert.Null(IcsDocument.MasterOf(IcsDocument.TryLoad(Ics.Events(("a", "20260914")))!));
    }

    [Fact]
    public void InstanceIdOf_IsTheLiteralRecurrenceId()
    {
        var calendar = IcsDocument.TryLoad(Ics.RuleWithOverride("FREQ=WEEKLY;COUNT=2", "20261130T090000"))!;

        Assert.Equal(string.Empty, IcsDocument.InstanceIdOf(IcsDocument.MasterOf(calendar)!));
        Assert.Equal("20260914T090000", IcsDocument.InstanceIdOf(calendar.Events.Single(e => e!.RecurrenceIdentifier is not null)!));
    }

    [Fact]
    public void InstanceIdOf_KeepsADateADate()
    {
        var calendar = IcsDocument.TryLoad(Ics.Events(("a", "20260914")))!;

        Assert.Equal("20260914", IcsDocument.InstanceIdOf(calendar.Events.Single()!));
    }
}
