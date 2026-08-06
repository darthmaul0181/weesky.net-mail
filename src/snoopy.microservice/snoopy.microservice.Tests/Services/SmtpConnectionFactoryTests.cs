using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpConnectionFactoryTests
{
    // Refused at once rather than resolved: the connection attempt is incidental to what is asserted.
    private const string ClosedPortHost = "127.0.0.1";
    private const int ClosedPort = 1;

    private static SmtpConnectionFactory CreateFactory(ILogger<SmtpConnectionFactory>? logger = null)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions());
        return new SmtpConnectionFactory(monitor.Object, logger ?? NullLogger<SmtpConnectionFactory>.Instance);
    }

    [Fact]
    public async Task OpenAsync_FailsWhenTheConnectionCarriesNoSmtpHost()
    {
        var connection = TestConnections.Primary("a@b.c", "pw") with { SmtpHost = "" };

        var result = await CreateFactory().OpenAsync(connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mail service is not configured", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ThrowsOnAMissingUsername()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateFactory().OpenAsync(TestConnections.Primary("", "pw"), CancellationToken.None));
    }

    [Fact]
    public async Task OpenAsync_OnACleartextEndpoint_WarnsNamingTheHost()
    {
        var logger = new CapturingLogger();
        var connection = TestConnections.Primary("a@b.c", "pw") with
        {
            SmtpHost = ClosedPortHost, SmtpPort = ClosedPort, SmtpSecurity = SecureSocketOptions.None
        };

        await CreateFactory(logger).OpenAsync(connection, CancellationToken.None);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(ClosedPortHost, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_OnAnEncryptedEndpoint_DoesNotWarn()
    {
        var logger = new CapturingLogger();
        var connection = TestConnections.Primary("a@b.c", "pw") with
        {
            SmtpHost = ClosedPortHost, SmtpPort = ClosedPort
        };

        await CreateFactory(logger).OpenAsync(connection, CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed class CapturingLogger : ILogger<SmtpConnectionFactory>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
