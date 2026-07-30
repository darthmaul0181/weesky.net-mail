using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class MailMessageSourceTests
{
    private const int Cap = 1024 * 1024;

    [Fact]
    public void IsTruncated_IsFalseBelowTheCap()
        => Assert.False(MailMessageSource.IsTruncated(Cap - 1, Cap));

    /// <summary>A message weighing exactly the cap arrived whole; labelling it truncated
    /// would tell the reader bytes are missing when none are.</summary>
    [Fact]
    public void IsTruncated_IsFalseAtExactlyTheCap()
        => Assert.False(MailMessageSource.IsTruncated(Cap, Cap));

    [Fact]
    public void IsTruncated_IsTrueAboveTheCap()
        => Assert.True(MailMessageSource.IsTruncated(Cap + 1, Cap));
}
