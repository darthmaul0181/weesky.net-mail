using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryMailboxTests
{
    [Theory]
    [InlineData(" Darth@Weesky.be ", "darth@weesky.be")]
    [InlineData("a.b+c@example.org", "a.b+c@example.org")]
    public void Normalizes_TrimmedAndLowered(string raw, string expected)
    {
        Assert.True(DeliveryMailbox.TryNormalize(raw, out var mailbox));
        Assert.Equal(expected, mailbox);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noatsign")]
    [InlineData("@weesky.be")]
    [InlineData("darth@")]
    [InlineData("two@at@weesky.be")]
    [InlineData("darth@weesky.be\r\nX-Forged: 1")]
    [InlineData("dar th@weesky.be")]
    public void Refuses_WhatIsNotOneAddress(string? raw) =>
        Assert.False(DeliveryMailbox.TryNormalize(raw, out _));

    [Fact]
    public void Refuses_Over320Characters() =>
        // "@x.be" is 5 chars: 316 + 5 = 321, one past MaxLength. The brief's own 315 lands on
        // exactly 320, which TryNormalize accepts (length > MaxLength, not >=).
        Assert.False(DeliveryMailbox.TryNormalize(new string('a', 316) + "@x.be", out _));
}
