using MailKit.Security;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Task 2: the user quota now comes from a live GETQUOTAROOT INBOX rather than the doveadm
/// HTTP API, so both the capability gate and the RFC 2087 unit conversion need a real
/// MailKit client talking to a server — <see cref="FakeImapServer"/>, shared with
/// <see cref="ImapSessionListFoldersTests"/>.
///
/// The session owns and disposes the underlying client (<see cref="ImapSession.DisposeAsync"/>),
/// so only the session and the fake server need disposing here.
/// </summary>
public sealed class ImapSessionQuotaTests
{
    private static async Task<(FakeImapServer server, ImapSession session, CancellationTokenSource cts)>
        ConnectAsync(bool quota, string? quotaResponse = null)
    {
        var server = new FakeImapServer(quota: quota, quotaResponse: quotaResponse);
        server.Start();

        var client = new MailKit.Net.Imap.ImapClient();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync("127.0.0.1", server.Port, SecureSocketOptions.None, cts.Token);
        await client.AuthenticateAsync("alice", "hunter2", cts.Token);

        var session = new ImapSession(client, Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>());
        return (server, session, cts);
    }

    [Fact]
    public async Task SupportsQuota_IsTrueWhenTheServerAdvertisesTheCapability()
    {
        var (server, session, cts) = await ConnectAsync(quota: true, quotaResponse: "STORAGE 0 0");
        using var __ = cts;
        using var _ = server;
        await using var ___ = session;

        Assert.True(session.SupportsQuota);
    }

    [Fact]
    public async Task SupportsQuota_IsFalseWithoutTheCapability()
    {
        var (server, session, cts) = await ConnectAsync(quota: false);
        using var __ = cts;
        using var _ = server;
        await using var ___ = session;

        Assert.False(session.SupportsQuota);
    }

    // RFC 2087 STORAGE/MESSAGE values are 1024-octet blocks; the model is bytes.
    [Fact]
    public async Task GetQuotaAsync_ConvertsStorageBlocksToBytes()
    {
        var (server, session, cts) = await ConnectAsync(quota: true, quotaResponse: "STORAGE 2048 10240 MESSAGE 5 100");
        using var __ = cts;
        using var _ = server;
        await using var ___ = session;

        var result = await session.GetQuotaAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2048L * 1024, result.Value.StorageBytesUsed);
        Assert.Equal(10240L * 1024, result.Value.StorageBytesLimit);
        Assert.Equal(5, result.Value.MessageCount);
        Assert.Equal(100, result.Value.MessageLimit);
    }

    // A resource the server never reported (no MESSAGE line here) means no limit — the
    // model's convention for "no limit" is 0, not null.
    [Fact]
    public async Task GetQuotaAsync_MapsAnAbsentResourceToZero()
    {
        var (server, session, cts) = await ConnectAsync(quota: true, quotaResponse: "STORAGE 100 0");
        using var __ = cts;
        using var _ = server;
        await using var ___ = session;

        var result = await session.GetQuotaAsync(cts.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(100L * 1024, result.Value.StorageBytesUsed);
        Assert.Equal(0, result.Value.StorageBytesLimit);
        Assert.Equal(0, result.Value.MessageCount);
        Assert.Equal(0, result.Value.MessageLimit);
    }
}
