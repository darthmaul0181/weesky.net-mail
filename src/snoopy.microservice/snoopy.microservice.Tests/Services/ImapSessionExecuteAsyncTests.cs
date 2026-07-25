using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Every IO method on ImapSession used to spell out the same three catch clauses; they now share
/// ExecuteAsync. The seventeen call sites have no coverage of their own — ImapSession takes a
/// concrete MailKit client, so driving them needs a live server — which is exactly why the failure
/// contract they all delegate to is pinned down here instead.
/// </summary>
public sealed class ImapSessionExecuteAsyncTests
{
    // Never connected: the constructor only reads PersonalNamespaces, and nothing below reaches
    // the wire — ExecuteAsync is handed the operation to run.
    private static ImapSession CreateSession(ILogger? logger = null) =>
        new(new ImapClient(), Mock.Of<IMailHtmlSanitizer>(), logger ?? Mock.Of<ILogger>());

    [Fact]
    public async Task ExecuteAsync_ReturnsWhatTheOperationReturns()
    {
        var result = await CreateSession().ExecuteAsync(CancellationToken.None,
            () => Task.FromResult(Result.Success("done")), "unused", _ => { });

        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Value);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesAFailureTheOperationItselfReturned()
    {
        var result = await CreateSession().ExecuteAsync(CancellationToken.None,
            () => Task.FromResult(Result.Failure<string>("target_not_selectable")), "unused", _ => { });

        Assert.True(result.IsFailure);
        Assert.Equal("target_not_selectable", result.Error);
    }

    // The server's own words never reach the client: one opaque sentence out, the detail logged.
    [Fact]
    public async Task ExecuteAsync_ReportsTheOpaqueMessageAndLogsTheException()
    {
        var boom = new InvalidOperationException("IMAP said something revealing");
        Exception? logged = null;

        var result = await CreateSession().ExecuteAsync<string>(CancellationToken.None,
            () => throw boom, "Unable to read the messages", ex => logged = ex);

        Assert.Equal("Unable to read the messages", result.Error);
        Assert.Same(boom, logged);
    }

    [Fact]
    public async Task ExecuteAsync_PrefersTheSentinelOverTheOpaqueMessage()
    {
        var logged = false;

        var result = await CreateSession().ExecuteAsync<string>(CancellationToken.None,
            () => throw new FolderNotFoundException("Archive"),
            "Unable to read the messages", _ => logged = true, ImapSession.FolderSentinel);

        Assert.Equal(ImapSession.FolderNotFound, result.Error);
        // A condition the caller is expected to handle is not an incident worth an error log.
        Assert.False(logged);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToTheOpaqueMessageWhenTheSentinelDoesNotMatch()
    {
        var result = await CreateSession().ExecuteAsync<string>(CancellationToken.None,
            () => throw new InvalidOperationException("something else"),
            "Unable to read the messages", _ => { }, ImapSession.FolderSentinel);

        Assert.Equal("Unable to read the messages", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_RethrowsWhenTheCallerCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateSession().ExecuteAsync<string>(cts.Token,
                () => throw new OperationCanceledException(cts.Token), "unused", _ => { }));
    }

    // A timeout inside MailKit also surfaces as OperationCanceledException. Nobody asked for it,
    // so it is an ordinary failure and must degrade rather than propagate as a cancellation.
    [Fact]
    public async Task ExecuteAsync_TreatsAnUnrequestedCancellationAsAnOrdinaryFailure()
    {
        var result = await CreateSession().ExecuteAsync<string>(CancellationToken.None,
            () => throw new OperationCanceledException(), "Unable to read the messages", _ => { });

        Assert.True(result.IsFailure);
        Assert.Equal("Unable to read the messages", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOnceTheSessionIsDisposed()
    {
        var session = CreateSession();
        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.ExecuteAsync(CancellationToken.None,
                () => Task.FromResult(Result.Success()), "unused", _ => { }));
    }

    [Theory]
    [InlineData(typeof(FolderNotFoundException), true, false)]
    [InlineData(typeof(MessageNotFoundException), false, true)]
    [InlineData(typeof(InvalidOperationException), false, false)]
    public void Sentinels_RecogniseOnlyTheirOwnCondition(Type exceptionType, bool folder, bool message)
    {
        Exception ex = exceptionType == typeof(FolderNotFoundException)
            ? new FolderNotFoundException("Archive")
            : exceptionType == typeof(MessageNotFoundException)
                ? new MessageNotFoundException("gone")
                : new InvalidOperationException("unrelated");

        Assert.Equal(folder ? ImapSession.FolderNotFound : null, ImapSession.FolderSentinel(ex));
        Assert.Equal(message ? ImapSession.MessageNotFound : null, ImapSession.MessageSentinel(ex));

        var expected = folder ? ImapSession.FolderNotFound : message ? ImapSession.MessageNotFound : null;
        Assert.Equal(expected, ImapSession.FolderOrMessageSentinel(ex));
    }
}
