using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OAuthHandshakeStoreTests
{
    private static OAuthHandshakeStore Create() => new(new MemoryCache(new MemoryCacheOptions()));

    private static OAuthTokenResponse Tokens() => new("at", "rt", 3600, null);

    [Fact]
    public void Start_MintsAnUnguessableStateAndAPkcePair()
    {
        var store = Create();
        var user = Guid.NewGuid();

        var first = store.Start(user, Guid.NewGuid(), null);
        var second = store.Start(user, Guid.NewGuid(), null);

        Assert.NotEqual(first.State, second.State);
        Assert.True(first.State.Length >= 22);
        Assert.NotEqual(first.CodeVerifier, first.CodeChallenge);
        Assert.DoesNotContain('=', first.CodeChallenge);
    }

    [Fact]
    public void Consume_AfterAttach_AnswersTheTokens()
    {
        var store = Create();
        var user = Guid.NewGuid();
        var started = store.Start(user, Guid.NewGuid(), null);

        Assert.True(store.Attach(started.State, Tokens(), "alice@outlook.test"));
        var consumed = store.Consume(started.State, user);

        Assert.Equal("rt", consumed!.Tokens!.RefreshToken);
        Assert.Equal("alice@outlook.test", consumed.Email);
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var store = Create();
        var user = Guid.NewGuid();
        var started = store.Start(user, Guid.NewGuid(), null);
        store.Attach(started.State, Tokens(), "alice@outlook.test");

        Assert.NotNull(store.Consume(started.State, user));
        Assert.Null(store.Consume(started.State, user));
    }

    [Fact]
    public void Consume_ByAnotherUser_AnswersNullAndDoesNotBurnTheEntry()
    {
        var store = Create();
        var user = Guid.NewGuid();
        var started = store.Start(user, Guid.NewGuid(), null);
        store.Attach(started.State, Tokens(), "alice@outlook.test");

        Assert.Null(store.Consume(started.State, Guid.NewGuid()));
        Assert.NotNull(store.Consume(started.State, user));
    }

    [Fact]
    public void Attach_ToAnUnknownState_AnswersFalse()
    {
        Assert.False(Create().Attach("nope", Tokens(), "alice@outlook.test"));
    }

    [Fact]
    public void Find_OfAnUnknownState_AnswersNull()
    {
        Assert.Null(Create().Find("nope"));
    }

    // Fails against the record-generated ToString, so deleting the redaction turns this red.
    [Fact]
    public void ToString_OfAHandshakeNeverPrintsItsSecrets()
    {
        var store = Create();
        var started = store.Start(Guid.NewGuid(), Guid.NewGuid(), null);
        store.Attach(started.State, Tokens(), "alice@outlook.test");

        var printed = store.Find(started.State)!.ToString();

        Assert.DoesNotContain(started.State, printed);
        Assert.DoesNotContain(started.CodeVerifier, printed);
        Assert.DoesNotContain("rt", printed);
        Assert.DoesNotContain("alice@outlook.test", printed);
    }
}
