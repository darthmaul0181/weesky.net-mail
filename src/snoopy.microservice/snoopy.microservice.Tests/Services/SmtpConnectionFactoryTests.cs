using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpConnectionFactoryTests
{
    private static SmtpConnectionFactory CreateFactory(MailOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new SmtpConnectionFactory(monitor.Object, NullLogger<SmtpConnectionFactory>.Instance);
    }

    [Fact]
    public async Task OpenAsync_FailsWhenSmtpIsNotConfigured()
    {
        var result = await CreateFactory(new MailOptions()).OpenAsync("a@b.c", "pw", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mail service is not configured", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ThrowsOnMissingEmail()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateFactory(new MailOptions()).OpenAsync("", "pw", CancellationToken.None));
    }
}
