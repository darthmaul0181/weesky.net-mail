using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A pool over a real factory over <see cref="PoolImapServer"/>, with a clock the test moves by
/// hand. The options object is shared by reference: mutate it to change the pool's behaviour
/// mid-test, exactly as a hot reload would. Disposing the host disposes the pool.
/// </summary>
internal sealed class PoolHost(ImapConnectionPool pool, MutableTimeProvider clock, MailOptions options) : IAsyncDisposable
{
    public ImapConnectionPool Pool { get; } = pool;
    public MutableTimeProvider Clock { get; } = clock;
    public MailOptions Options { get; } = options;

    public ValueTask DisposeAsync() => Pool.DisposeAsync();
}

internal static class PoolTestHost
{
    public static PoolHost Create(PoolImapServer server, Action<MailOptions>? configure = null)
    {
        var options = new MailOptions { TimeoutSeconds = 10, AllowCleartext = true, PoolHealthTimeoutSeconds = 2 };
        configure?.Invoke(options);
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        var factory = new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
        var clock = new MutableTimeProvider();
        var pool = new ImapConnectionPool(
            factory, new CredentialFingerprint(), monitor.Object, clock, NullLogger<ImapConnectionPool>.Instance);
        return new PoolHost(pool, clock, options);
    }

    public static MailAccountConnection Connection(PoolImapServer server, string email, string password) =>
        TestConnections.Primary(email, password) with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };

    /// <summary>A local shared mailbox: two users, one (host, user, secret) — one pool entry.</summary>
    public static MailAccountConnection Shared(PoolImapServer server, string accountId, string email, string password) =>
        TestConnections.ConnectedLocal(accountId, email, password) with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };
}
