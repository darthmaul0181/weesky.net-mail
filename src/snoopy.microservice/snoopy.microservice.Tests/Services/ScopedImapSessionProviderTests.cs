using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The connection-per-request contract. It used to be one connection per repository *method*,
/// so a single rename or send paid two or three TCP + TLS + SASL handshakes against the mail
/// server; these tests are what keep that from coming back.
/// </summary>
public sealed class ScopedImapSessionProviderTests
{
    private readonly Mock<IImapConnectionFactory> _factory = new();
    private readonly Mock<IImapSession> _session = new();

    private ScopedImapSessionProvider CreateSut()
    {
        _factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success<IImapSession>(_session.Object));
        return new ScopedImapSessionProvider(_factory.Object, Mock.Of<ILogger<ScopedImapSessionProvider>>());
    }

    [Fact]
    public async Task GetAsync_OpensTheConnectionOnlyOnceForTheWholeScope()
    {
        await using var sut = CreateSut();

        var first = await sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None);
        var second = await sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None);

        Assert.Same(first.Value, second.Value);
        _factory.Verify(f => f.OpenAsync("alice@weesky.be", "hunter2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConcurrentGets_StillOpenOnlyOneConnection()
    {
        await using var sut = CreateSut();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None)));

        _factory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ClosesTheSession()
    {
        var sut = CreateSut();
        await sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None);

        await sut.DisposeAsync();

        _session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var sut = CreateSut();
        await sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None);

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        _session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithoutAnySession_DoesNothing()
    {
        var sut = CreateSut();

        await sut.DisposeAsync();

        _factory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The request is over either way: a teardown that throws must not become its outcome.
    [Fact]
    public async Task DisposeAsync_SwallowsAFailingTeardown()
    {
        var sut = CreateSut();
        _session.Setup(s => s.DisposeAsync()).Throws(new IOException("connection already gone"));
        await sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None);

        await sut.DisposeAsync();
    }

    // One refused authentication must not be retried once per operation in the same request.
    [Fact]
    public async Task GetAsync_RemembersAFailureInsteadOfReconnecting()
    {
        _factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));
        await using var sut = new ScopedImapSessionProvider(
            _factory.Object, Mock.Of<ILogger<ScopedImapSessionProvider>>());

        var first = await sut.GetAsync("alice@weesky.be", "wrong", CancellationToken.None);
        var second = await sut.GetAsync("alice@weesky.be", "wrong", CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.Equal("Mail authentication failed", second.Error);
        _factory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // A cached session authenticates as one specific user; different credentials must never reuse it.
    [Fact]
    public async Task GetAsync_WithDifferentCredentials_ReplacesAndClosesThePreviousSession()
    {
        await using var sut = CreateSut();
        await sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None);

        await sut.GetAsync("bob@weesky.be", "swordfish", CancellationToken.None);

        _session.Verify(s => s.DisposeAsync(), Times.Once);
        _factory.Verify(f => f.OpenAsync("bob@weesky.be", "swordfish", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_AfterDispose_Throws()
    {
        var sut = CreateSut();
        await sut.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.GetAsync("alice@weesky.be", "hunter2", CancellationToken.None));
    }
}
