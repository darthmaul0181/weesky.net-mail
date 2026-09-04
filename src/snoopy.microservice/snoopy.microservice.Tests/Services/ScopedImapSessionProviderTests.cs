using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The connection-per-request contract. It used to be one connection per repository *method*,
/// so a single rename or send paid two or three TCP + TLS + SASL handshakes against the mail
/// server; these tests are what keep that from coming back.
/// </summary>
public sealed class ScopedImapSessionProviderTests
{
    private static readonly MailAccountConnection Alice = TestConnections.Primary("alice@weesky.be", "hunter2");
    private static readonly MailAccountConnection Bob = TestConnections.Primary("bob@weesky.be", "swordfish");

    private readonly Mock<IImapConnectionFactory> _factory = new();
    private readonly Mock<IImapConnectionPool> _pool = new();
    private readonly RequestIdentity _identity = new();
    private readonly Mock<IImapSession> _session = new();

    private ScopedImapSessionProvider CreateSut()
    {
        _factory.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success<IImapSession>(_session.Object));
        _pool.Setup(p => p.BorrowAsync(It.IsAny<MailAccountConnection>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success<IImapSession>(_session.Object));
        return new ScopedImapSessionProvider(
            _factory.Object, _pool.Object, _identity, Mock.Of<ILogger<ScopedImapSessionProvider>>());
    }

    [Fact]
    public async Task GetAsync_OpensTheConnectionOnlyOnceForTheWholeScope()
    {
        await using var sut = CreateSut();

        var first = await sut.GetAsync(Alice, CancellationToken.None);
        var second = await sut.GetAsync(Alice, CancellationToken.None);

        Assert.Same(first.Value, second.Value);
        _factory.Verify(f => f.OpenAsync(Alice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConcurrentGets_StillOpenOnlyOneConnection()
    {
        await using var sut = CreateSut();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            sut.GetAsync(Alice, CancellationToken.None)));

        _factory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ClosesTheSession()
    {
        var sut = CreateSut();
        await sut.GetAsync(Alice, CancellationToken.None);

        await sut.DisposeAsync();

        _session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var sut = CreateSut();
        await sut.GetAsync(Alice, CancellationToken.None);

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        _session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithoutAnySession_DoesNothing()
    {
        var sut = CreateSut();

        await sut.DisposeAsync();

        _factory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The request is over either way: a teardown that throws must not become its outcome.
    [Fact]
    public async Task DisposeAsync_SwallowsAFailingTeardown()
    {
        var sut = CreateSut();
        _session.Setup(s => s.DisposeAsync()).Throws(new IOException("connection already gone"));
        await sut.GetAsync(Alice, CancellationToken.None);

        await sut.DisposeAsync();
    }

    // One refused authentication must not be retried once per operation in the same request.
    [Fact]
    public async Task GetAsync_RemembersAFailureInsteadOfReconnecting()
    {
        _factory.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));
        await using var sut = new ScopedImapSessionProvider(
            _factory.Object, _pool.Object, _identity, Mock.Of<ILogger<ScopedImapSessionProvider>>());

        var wrong = TestConnections.Primary("alice@weesky.be", "wrong");
        var first = await sut.GetAsync(wrong, CancellationToken.None);
        var second = await sut.GetAsync(wrong, CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.Equal("Mail authentication failed", second.Error);
        _factory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // A cached session authenticates as one specific account; a different one must never reuse it.
    [Fact]
    public async Task GetAsync_WithADifferentAccount_ReplacesAndClosesThePreviousSession()
    {
        await using var sut = CreateSut();
        await sut.GetAsync(Alice, CancellationToken.None);

        await sut.GetAsync(Bob, CancellationToken.None);

        _session.Verify(s => s.DisposeAsync(), Times.Once);
        _factory.Verify(f => f.OpenAsync(Bob, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_AfterDispose_Throws()
    {
        var sut = CreateSut();
        await sut.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.GetAsync(Alice, CancellationToken.None));
    }

    // Test 13 of the spec: without a request identity the pool is never consulted.
    [Fact]
    public async Task GetAsync_WithoutARequestIdentity_OpensASingleUseConnection()
    {
        await using var sut = CreateSut();

        await sut.GetAsync(Alice, CancellationToken.None);

        _factory.Verify(f => f.OpenAsync(Alice, It.IsAny<CancellationToken>()), Times.Once);
        _pool.Verify(p => p.BorrowAsync(It.IsAny<MailAccountConnection>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WithARequestIdentity_BorrowsFromThePoolAsThatUser()
    {
        var uid = Guid.NewGuid();
        _identity.Set(uid);
        await using var sut = CreateSut();

        await sut.GetAsync(Alice, CancellationToken.None);

        _pool.Verify(p => p.BorrowAsync(Alice, uid, It.IsAny<CancellationToken>()), Times.Once);
        _factory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
