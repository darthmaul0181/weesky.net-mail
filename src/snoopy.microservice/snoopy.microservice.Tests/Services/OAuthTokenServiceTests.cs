using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OAuthTokenServiceTests
{
    private static readonly byte[] Kek =
        ConnectedAccountCipher.DeriveKek("main", ConnectedAccountCipher.NewSalt());

    private static OAuthProviderConfig Provider() => new(
        "https://provider.test/authorize", "https://provider.test/token",
        "offline_access", "client-id", [9, 9, 9]);

    private static ConnectedAccount Row(string refreshToken)
    {
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DomainId = Guid.NewGuid(),
            Email = "alice@outlook.test", AuthMode = MailAuthMode.OAuth2
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            Kek, refreshToken, ConnectedAccountCipher.Context(row));
        return row;
    }

    private static (OAuthTokenService Service, Mock<IConnectedAccountStore> Accounts) Create(
        StubHttpMessageHandler handler)
    {
        var accounts = new Mock<IConnectedAccountStore>();
        var protector = new Mock<IClientSecretProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns("client-secret");

        var service = new OAuthTokenService(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            accounts.Object,
            protector.Object,
            NullLogger<OAuthTokenService>.Instance);

        return (service, accounts);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshesAndAnswersTheAccessToken()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at-1", result.Value);
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0]);
        Assert.Contains("client_secret=client-secret", handler.Bodies[0]);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RewritesTheCipherWhenTheRefreshTokenRotates()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""));
        var (service, accounts) = Create(handler);
        var row = Row("rt-1");

        await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        accounts.Verify(a => a.UpdateCipherAsync(
            row,
            It.Is<byte[]>(c => ConnectedAccountCipher.Decrypt(
                Kek, c, ConnectedAccountCipher.Context(row)).Value == "rt-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAccessTokenAsync_LeavesTheCipherAloneWhenNoTokenRotates()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}"""));
        var (service, accounts) = Create(handler);

        await service.GetAccessTokenAsync(Row("rt-1"), Provider(), Kek, CancellationToken.None);

        accounts.Verify(a => a.UpdateCipherAsync(
            It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesSoASecondCallDoesNotExchange()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}"""));
        var (service, _) = Create(handler);
        var row = Row("rt-1");

        await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);
        var again = await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        Assert.Equal("at-1", again.Value);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_UnderABurst_ExchangesExactlyOnce()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}"""));
        var (service, _) = Create(handler);
        var row = Row("rt-1");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None)));

        Assert.All(results, r => Assert.Equal("at-1", r.Value));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_OnInvalidGrant_AnswersCredentialsInvalid()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public async Task GetAccessTokenAsync_OnAServerError_AnswersProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.ProviderUnavailable, result.Error);
    }

    // The caller's disconnect after the provider consumed the old refresh token must not abandon
    // the rotated one — the frontend aborts requests on unmount, so this is an everyday event.
    [Fact]
    public async Task GetAccessTokenAsync_CancelledMidExchange_StillPersistsTheRotatedToken()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(() =>
        {
            cts.Cancel();
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}""")();
        });
        var (service, accounts) = Create(handler);
        accounts.Setup(a => a.UpdateCipherAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns((ConnectedAccount _, byte[] _, CancellationToken token) =>
            {
                // EF's SaveChangesAsync honors the token; the write must not receive a dead one.
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var row = Row("rt-1");

        var result = await service.GetAccessTokenAsync(row, Provider(), Kek, cts.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("at-1", result.Value);
        accounts.Verify(a => a.UpdateCipherAsync(
            row,
            It.Is<byte[]>(c => ConnectedAccountCipher.Decrypt(
                Kek, c, ConnectedAccountCipher.Context(row)).Value == "rt-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // A rotation write the database refused must not drop the rotated token: the process still
    // holds it, and the next refresh must exchange with it — not the consumed one — and persist.
    [Fact]
    public async Task GetAccessTokenAsync_WhenTheRotationWriteFails_TheNextRefreshUsesTheRotatedToken()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-2","refresh_token":"rt-3","expires_in":3600}"""));
        var (service, accounts) = Create(handler);
        var row = Row("rt-1");
        accounts.SetupSequence(a => a.UpdateCipherAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None));
        var again = await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        Assert.Equal("at-2", again.Value);
        Assert.Contains("refresh_token=rt-2", handler.Bodies[1]);
        accounts.Verify(a => a.UpdateCipherAsync(
            row,
            It.Is<byte[]>(c => ConnectedAccountCipher.Decrypt(
                Kek, c, ConnectedAccountCipher.Context(row)).Value == "rt-3"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // The provider may answer the retry without rotating again; the write owed from the failed
    // rotation must still land, or the row keeps the consumed token forever.
    [Fact]
    public async Task GetAccessTokenAsync_RetriesTheOwedWriteWhenTheProviderRotatesNoFurther()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-2","expires_in":3600}"""));
        var (service, accounts) = Create(handler);
        var row = Row("rt-1");
        accounts.SetupSequence(a => a.UpdateCipherAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None));
        var again = await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        Assert.Equal("at-2", again.Value);
        Assert.Contains("refresh_token=rt-2", handler.Bodies[1]);
        accounts.Verify(a => a.UpdateCipherAsync(
            row,
            It.Is<byte[]>(c => ConnectedAccountCipher.Decrypt(
                Kek, c, ConnectedAccountCipher.Context(row)).Value == "rt-2"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // A row rewritten after the stash — re-consent is the user's only remedy for a dead refresh
    // token — must win over a stash that still opens, or the account is broken until restart.
    [Fact]
    public async Task GetAccessTokenAsync_ARowRewrittenAfterTheStash_WinsOverTheStash()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-2","expires_in":3600}"""));
        var (service, accounts) = Create(handler);
        var row = Row("rt-1");
        accounts.Setup(a => a.UpdateCipherAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None));

        // The re-consent: a fresh refresh token written to the same row, under the same KEK.
        row.Cipher = ConnectedAccountCipher.Encrypt(Kek, "rt-9", ConnectedAccountCipher.Context(row));

        var again = await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        Assert.Equal("at-2", again.Value);
        Assert.Contains("refresh_token=rt-9", handler.Bodies[1]);
    }

    // Pins that a genuine HttpClient timeout still maps to 502 now that the refresh runs under
    // CancellationToken.None: the OCE rethrow filter checks the caller's token, never the client's.
    [Fact]
    public async Task GetAccessTokenAsync_OnAGenuineTimeout_AnswersProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler(
            () => throw new TaskCanceledException("The request was canceled due to the configured timeout."));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.ProviderUnavailable, result.Error);
    }

    // A stash re-keyed out from under it (ChangeSecret re-encrypts the row under a new KEK) must
    // not mask the row: a pending cipher that no longer opens is ignored, never persisted.
    [Fact]
    public async Task GetAccessTokenAsync_AStashTheKekNoLongerOpens_FallsBackToTheRow()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""),
            StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{"access_token":"at-2","expires_in":3600}"""));
        var (service, accounts) = Create(handler);
        var row = Row("rt-1");
        accounts.Setup(a => a.UpdateCipherAsync(
                It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None));

        // The re-key: the row now opens under a new KEK; the stashed rt-2 cipher does not.
        var newKek = ConnectedAccountCipher.DeriveKek("new-main", ConnectedAccountCipher.NewSalt());
        row.Cipher = ConnectedAccountCipher.Encrypt(newKek, "rt-1", ConnectedAccountCipher.Context(row));

        var again = await service.GetAccessTokenAsync(row, Provider(), newKek, CancellationToken.None);

        Assert.Equal("at-2", again.Value);
        Assert.Contains("refresh_token=rt-1", handler.Bodies[1]);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheCipherDoesNotOpen_AnswersCredentialsInvalid()
    {
        var handler = new StubHttpMessageHandler();
        var (service, _) = Create(handler);
        var otherKek = ConnectedAccountCipher.DeriveKek("other", ConnectedAccountCipher.NewSalt());

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), otherKek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheClientSecretDoesNotOpen_AnswersProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler();
        var accounts = new Mock<IConnectedAccountStore>();
        var protector = new Mock<IClientSecretProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((string?)null);
        var service = new OAuthTokenService(
            new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()),
            accounts.Object, protector.Object, NullLogger<OAuthTokenService>.Instance);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.ProviderUnavailable, result.Error);
        Assert.Equal(0, handler.Calls);
    }

    // Fails against the record-generated ToString, so deleting the redaction turns this red.
    [Fact]
    public void ToString_OfATokenResponseNeverPrintsTheTokens()
    {
        var printed = new OAuthTokenResponse(
            "the-access-token", "the-refresh-token", 3600, "the-id-token").ToString();

        Assert.DoesNotContain("the-access-token", printed);
        Assert.DoesNotContain("the-refresh-token", printed);
        Assert.DoesNotContain("the-id-token", printed);
    }
}
