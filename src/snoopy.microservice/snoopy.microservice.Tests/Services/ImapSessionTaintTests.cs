using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// A session that met an exception or a cancellation mid-command may have left the protocol out
/// of sync; the pool must never reuse that socket. The session records it, and hands the verdict
/// to whoever releases the client.
/// </summary>
public sealed class ImapSessionTaintTests
{
    private static ImapSession CreateSession(ImapClientRelease? release = null) =>
        new(new ImapClient(), Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>(), release);

    [Fact]
    public async Task ExecuteAsync_OnAnUnrecognisedException_Taints()
    {
        var session = CreateSession();

        await session.ExecuteAsync<string>(CancellationToken.None,
            () => throw new IOException("stream torn"), "opaque", _ => { });

        Assert.True(session.Tainted);
    }

    // A tagged NO after a clean exchange: the socket is fine, and the caller handles the sentinel.
    [Fact]
    public async Task ExecuteAsync_OnASentinel_DoesNotTaint()
    {
        var session = CreateSession();

        await session.ExecuteAsync<string>(CancellationToken.None,
            () => throw new FolderNotFoundException("Archive"), "opaque", _ => { }, ImapSession.FolderSentinel);

        Assert.False(session.Tainted);
    }

    [Fact]
    public async Task ExecuteAsync_OnCancellation_TaintsThenRethrows()
    {
        var session = CreateSession();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.ExecuteAsync<string>(cts.Token, () => throw new OperationCanceledException(cts.Token), "opaque", _ => { }));

        Assert.True(session.Tainted);
    }

    [Fact]
    public async Task ExecuteAsync_OnSuccess_StaysClean()
    {
        var session = CreateSession();

        await session.ExecuteAsync(CancellationToken.None, () => Task.FromResult(Result.Success("ok")), "opaque", _ => { });

        Assert.False(session.Tainted);
    }

    [Fact]
    public async Task DisposeAsync_HandsTheClientAndTheVerdictToTheRelease()
    {
        ImapClient? released = null;
        bool? healthy = null;
        var session = CreateSession((client, ok) => { released = client; healthy = ok; return ValueTask.CompletedTask; });
        await session.ExecuteAsync<string>(CancellationToken.None, () => throw new IOException(), "opaque", _ => { });

        await session.DisposeAsync();

        Assert.NotNull(released);
        Assert.False(healthy);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesOnlyOnce()
    {
        var releases = 0;
        var session = CreateSession((_, _) => { releases++; return ValueTask.CompletedTask; });

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, releases);
    }

    // The default release, on the wire: a tainted socket gets no LOGOUT — nothing in sync is there
    // to hear it — and is closed all the same.
    [Fact]
    public async Task DisposeAsync_OfATaintedSession_ClosesTheSocketWithoutLogout()
    {
        using var server = new PoolImapServer();
        server.Start();
        var client = await ConnectAsync(server);
        var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());
        await session.ExecuteAsync<string>(
            CancellationToken.None, () => throw new IOException("stream torn"), "opaque", _ => { });

        await session.DisposeAsync();

        Assert.False(
            await server.WaitUntilAsync(
                () => server.Commands.Any(c => c.Contains("LOGOUT", StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromMilliseconds(300)),
            "a session that ended in doubt must be closed without a LOGOUT");
        Assert.Equal(0, server.Logouts);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_OfACleanSession_LogsOutPolitely()
    {
        using var server = new PoolImapServer();
        server.Start();
        var client = await ConnectAsync(server);
        var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());

        await session.DisposeAsync();

        Assert.Equal(1, server.Logouts);
        Assert.False(client.IsConnected);
    }

    private static async Task<ImapClient> ConnectAsync(PoolImapServer server)
    {
        var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None);
        await client.AuthenticateAsync("alice@weesky.be", "hunter2");
        return client;
    }
}
