using CSharpFunctionalExtensions;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Calendar.Scheduling;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class SchedulingAccountControllerTests
{
    private const string StoredPassword = "st0red-pw";

    private readonly string _database = Guid.NewGuid().ToString("N");
    private readonly ServiceAccountSecretProtector _protector =
        new(DataProtectionProvider.Create(nameof(SchedulingAccountControllerTests)));
    private readonly Mock<IServiceAccountProvider> _accounts = new();
    private readonly Mock<ISmtpConnectionFactory> _smtp = new();
    private readonly Mock<ISmtpSession> _session = new();
    private readonly MutableTimeProvider _clock = new();
    private MailOptions _mail = new();

    private SchedulingAccountStore Store() => new(new PreferencesTestDbContext(_database));

    private SchedulingAccountController CreateController(ISchedulingAccountStore? store = null)
    {
        var mail = new Mock<IOptionsMonitor<MailOptions>>();
        mail.SetupGet(m => m.CurrentValue).Returns(() => _mail);
        return new SchedulingAccountController(store ?? Store(), _protector, _accounts.Object, _smtp.Object, mail.Object, _clock);
    }

    private static SchedulingAccountRequest Request(
        string? host = "smtp.weesky.be", int port = 587, string? security = "StartTls",
        string? login = "noreply-agenda@weesky.net", string? password = "n3w-pw") =>
        new(host, port, security, login, password);

    /// <summary>The stored endpoint, so that leaving the password empty is allowed.</summary>
    private static SchedulingAccountRequest ToStoredEndpoint(string? password = null, string? login = "renamed@weesky.net", string? security = "StartTls") =>
        Request(host: "stored.weesky.be", port: 465, security: security, login: login, password: password);

    private Task StoreAsync(byte[]? cipher = null, string security = "SslOnConnect") =>
        Store().SaveAsync("stored.weesky.be", 465, security, "stored@weesky.net",
            cipher ?? _protector.Protect(StoredPassword), CancellationToken.None);

    private Task<SchedulingServiceAccount?> RowAsync() => Store().FindAsync(CancellationToken.None);

    private void OpensFail(string error) =>
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ISmtpSession>(error));

    private List<MailAccountConnection> OpensSucceed()
    {
        List<MailAccountConnection> opened = [];
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback<MailAccountConnection, CancellationToken>((c, _) => opened.Add(c))
            .ReturnsAsync(Result.Success(_session.Object));
        return opened;
    }

    private static SchedulingAccountTestResult TestResult(ActionResult<SchedulingAccountTestResult> result) =>
        Assert.IsType<SchedulingAccountTestResult>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // ---- GET ----

    [Fact]
    public async Task Get_WithNothingStored_SaysSo()
    {
        var result = await CreateController().GetSchedulingAccount(CancellationToken.None);

        var body = Assert.IsType<SchedulingAccountResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new SchedulingAccountResponse(false, null, null, null, null, false, false, false, null, null), body);
    }

    // The dialog offers "None" only where saving it can succeed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_SaysWhetherAnUnencryptedEndpointIsAccepted(bool allowCleartext)
    {
        _mail = new MailOptions { AllowCleartext = allowCleartext };
        await StoreAsync();

        var result = await CreateController().GetSchedulingAccount(CancellationToken.None);

        Assert.Equal(allowCleartext, Assert.IsType<SchedulingAccountResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).AllowCleartext);
    }

    [Fact]
    public async Task Get_DescribesTheStoredAccount_WithoutItsPassword()
    {
        await StoreAsync();
        var row = (await RowAsync())!;
        var testedAt = new DateTime(2026, 9, 14, 8, 30, 0, DateTimeKind.Unspecified);
        await Store().RecordTestAsync(testedAt, ok: true, row.UpdatedAt, CancellationToken.None);

        var result = await CreateController().GetSchedulingAccount(CancellationToken.None);

        var body = Assert.IsType<SchedulingAccountResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new SchedulingAccountResponse(true, "stored.weesky.be", 465, "SslOnConnect", "stored@weesky.net",
            PasswordStored: true, PasswordReadable: true, AllowCleartext: false, testedAt, true), body);
        Assert.Equal(DateTimeKind.Utc, body.LastTestAt!.Value.Kind);
        Assert.DoesNotContain(StoredPassword, body.ToString());
        Assert.DoesNotContain(typeof(SchedulingAccountResponse).GetProperties(), p => p.Name == "Password");
    }

    [Fact]
    public async Task Get_OnAnUndecryptablePassword_ReportsItUnreadable()
    {
        await StoreAsync(cipher: [1, 2, 3, 4]);

        var result = await CreateController().GetSchedulingAccount(CancellationToken.None);

        var body = Assert.IsType<SchedulingAccountResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(body.Configured);
        Assert.True(body.PasswordStored);
        Assert.False(body.PasswordReadable);
    }

    // ---- PUT ----

    [Fact]
    public async Task Put_StoresTheAccount_WithItsPasswordProtected_AndInvalidatesTheCache()
    {
        using var request = new CancellationTokenSource();
        var result = await CreateController().SaveSchedulingAccount(Request(login: "  noreply-agenda@weesky.net "), request.Token);

        Assert.IsType<NoContentResult>(result);
        var row = (await RowAsync())!;
        Assert.Equal(("smtp.weesky.be", 587, "StartTls", "noreply-agenda@weesky.net"), (row.Host, row.Port, row.Security, row.Login));
        Assert.Equal("n3w-pw", _protector.Unprotect(row.PasswordCipher));
        // Not the request's token: once the row is committed, an aborted request must not leave the cache unknown.
        _accounts.Verify(a => a.InvalidateAsync(CancellationToken.None), Times.Once);
        _accounts.VerifyNoOtherCalls();
    }

    public static TheoryData<SchedulingAccountRequest> InvalidRequests => new()
    {
        Request(host: null),
        Request(host: ""),
        Request(host: "http://evil.example/"),
        Request(host: new string('a', 256)),
        Request(port: 0),
        Request(port: 65536),
        Request(security: null),
        Request(security: "starttls"),
        Request(security: "1"),
        Request(security: "Auto"),
        Request(security: "None"),
        Request(login: null),
        Request(login: "   "),
        Request(login: new string('a', 310) + "@weesky.net"),
        Request(password: new string('é', 257)),
        Request(password: new string('a', 513)),
    };

    [Fact]
    public async Task Put_AcceptsAPasswordOfExactly512Bytes()
    {
        Assert.IsType<NoContentResult>(await CreateController().SaveSchedulingAccount(Request(password: new string('a', 512)), CancellationToken.None));

        Assert.Equal(512, _protector.Unprotect((await RowAsync())!.PasswordCipher)!.Length);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Put_RefusesAnInvalidAccount_AndStoresNothing(SchedulingAccountRequest request)
    {
        var result = await CreateController().SaveSchedulingAccount(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(ResultState.Error, Assert.IsType<ResultEnveloppe>(bad.Value).State);
        Assert.Null(await RowAsync());
        _accounts.Verify(a => a.InvalidateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Reachable from the form: a stored None stays selectable after the opt-in was turned off.
    [Fact]
    public async Task Put_RefusesNone_WithoutTheCleartextOptIn_WithAStableCode()
    {
        var result = await CreateController().SaveSchedulingAccount(Request(security: "None"), CancellationToken.None);

        Assert.Equal("security_none_disallowed", Assert.IsType<ResultEnveloppe>(Assert.IsType<BadRequestObjectResult>(result).Value).Message);
    }

    [Fact]
    public async Task Put_AcceptsNone_UnderTheCleartextOptIn()
    {
        _mail = new MailOptions { AllowCleartext = true };

        Assert.IsType<NoContentResult>(await CreateController().SaveSchedulingAccount(Request(security: "None"), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Put_WithoutAPassword_WhenNoneIsStored_IsRefused(string? password)
    {
        var result = await CreateController().SaveSchedulingAccount(Request(password: password), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("password_required", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        Assert.Null(await RowAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Put_WithoutAPassword_OnTheSameEndpoint_KeepsTheStoredOne(string? password)
    {
        await StoreAsync();

        var result = await CreateController().SaveSchedulingAccount(ToStoredEndpoint(password), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var row = (await RowAsync())!;
        Assert.Equal(("stored.weesky.be", "renamed@weesky.net", "StartTls"), (row.Host, row.Login, row.Security));
        Assert.Equal(StoredPassword, _protector.Unprotect(row.PasswordCipher));
    }

    // The stored password must never be carried to a host or port the admin just typed.
    [Theory]
    [InlineData("elsewhere.example", 465)]
    [InlineData("stored.weesky.be", 587)]
    public async Task Put_WithoutAPassword_OnAnotherEndpoint_IsRefused(string host, int port)
    {
        await StoreAsync();

        var result = await CreateController().SaveSchedulingAccount(Request(host: host, port: port, password: ""), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("password_required_for_new_endpoint", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        Assert.Equal(465, (await RowAsync())!.Port);
    }

    [Fact]
    public async Task Put_WithoutAPassword_OnTheSameHostSpelledInCapitals_KeepsTheStoredOne()
    {
        await StoreAsync();

        var result = await CreateController().SaveSchedulingAccount(
            Request(host: "Stored.Weesky.BE", port: 465, password: null), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Put_WithoutAPassword_OverAnUnreadableOne_IsRefused()
    {
        await StoreAsync(cipher: [1, 2, 3, 4]);

        var result = await CreateController().SaveSchedulingAccount(ToStoredEndpoint(""), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("password_unreadable", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        Assert.Equal("stored.weesky.be", (await RowAsync())!.Host);
    }

    [Fact]
    public async Task Put_ClearsTheLastTest()
    {
        await StoreAsync();
        await Store().RecordTestAsync(DateTime.UtcNow, ok: true, (await RowAsync())!.UpdatedAt, CancellationToken.None);

        await CreateController().SaveSchedulingAccount(Request(), CancellationToken.None);

        var row = (await RowAsync())!;
        Assert.Null(row.LastTestAt);
        Assert.Null(row.LastTestOk);
    }

    [Fact]
    public async Task Put_WithoutABody_IsRefused()
    {
        Assert.IsType<BadRequestObjectResult>(await CreateController().SaveSchedulingAccount(null!, CancellationToken.None));
    }

    // ---- DELETE ----

    [Fact]
    public async Task Delete_IsIdempotent_AndInvalidatesTheCacheEachTime()
    {
        await StoreAsync();

        using var request = new CancellationTokenSource();
        Assert.IsType<NoContentResult>(await CreateController().DeleteSchedulingAccount(request.Token));
        Assert.IsType<NoContentResult>(await CreateController().DeleteSchedulingAccount(request.Token));

        Assert.Null(await RowAsync());
        _accounts.Verify(a => a.InvalidateAsync(CancellationToken.None), Times.Exactly(2));
        _accounts.VerifyNoOtherCalls();
    }

    // ---- concurrent writes: never a 500 ----

    private SchedulingAccountStore Interleaved(Func<Task> otherWriter, int races = 1) =>
        new(new InterleavedPreferencesDbContext(_database, otherWriter, races));

    private Task ConcurrentSaveAsync(string host) =>
        Store().SaveAsync(host, 465, "SslOnConnect", "concurrent@weesky.net", _protector.Protect("c0ncurrent"), CancellationToken.None);

    private Task ConcurrentDeleteAsync() => Store().DeleteAsync(CancellationToken.None);

    [Fact]
    public async Task PutPut_TheLaterSaveAnswers204_AndItsValuesStay()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(() => ConcurrentSaveAsync("stored.weesky.be")));

        var result = await controller.SaveSchedulingAccount(ToStoredEndpoint(password: "l4ter"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var row = (await RowAsync())!;
        Assert.Equal(("renamed@weesky.net", "StartTls"), (row.Login, row.Security));
        Assert.Equal("l4ter", _protector.Unprotect(row.PasswordCipher));
    }

    // The keep-password rule was checked against a host another admin has since replaced: keeping
    // "the stored password" now would pair that admin's password with this save's host.
    [Fact]
    public async Task PutPut_KeepingThePassword_WhileAConcurrentSaveMovesTheHost_AnswersTheConflict()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(() => ConcurrentSaveAsync("elsewhere.example")));

        var result = await controller.SaveSchedulingAccount(ToStoredEndpoint(password: null), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(SchedulingAccountController.ChangedConcurrently, Assert.IsType<ResultEnveloppe>(conflict.Value).Message);
        var row = (await RowAsync())!;
        Assert.Equal(("elsewhere.example", "concurrent@weesky.net"), (row.Host, row.Login));
        Assert.Equal("c0ncurrent", _protector.Unprotect(row.PasswordCipher));
        _accounts.Verify(a => a.InvalidateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Same host, but not the row it checked: the password it would keep is no longer the one it saw.
    [Fact]
    public async Task PutPut_KeepingThePassword_WhileAConcurrentSaveKeepsTheHost_AnswersTheConflictToo()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(() => ConcurrentSaveAsync("stored.weesky.be")));

        var result = await controller.SaveSchedulingAccount(ToStoredEndpoint(password: ""), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var row = (await RowAsync())!;
        Assert.Equal(("stored.weesky.be", "concurrent@weesky.net"), (row.Host, row.Login));
        Assert.Equal("c0ncurrent", _protector.Unprotect(row.PasswordCipher));
    }

    [Fact]
    public async Task PutDelete_ASaveCarryingAPassword_Answers204_AndRecreatesTheAccount()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(ConcurrentDeleteAsync));

        Assert.IsType<NoContentResult>(await controller.SaveSchedulingAccount(Request(), CancellationToken.None));

        Assert.Equal("smtp.weesky.be", (await RowAsync())!.Host);
    }

    [Fact]
    public async Task PutDelete_ASaveKeepingThePassword_AnswersTheConflict()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(ConcurrentDeleteAsync));

        var result = await controller.SaveSchedulingAccount(ToStoredEndpoint(password: null), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(SchedulingAccountController.ChangedConcurrently, Assert.IsType<ResultEnveloppe>(conflict.Value).Message);
        Assert.Null(await RowAsync());
    }

    [Fact]
    public async Task PutPut_LosingTwice_AnswersTheConflict_Not500()
    {
        await StoreAsync();
        var round = 0;
        var controller = CreateController(Interleaved(() => ConcurrentSaveAsync($"concurrent{++round}.weesky.be"), races: 2));

        var result = await controller.SaveSchedulingAccount(Request(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(SchedulingAccountController.ChangedConcurrently, Assert.IsType<ResultEnveloppe>(conflict.Value).Message);
        Assert.Equal("concurrent2.weesky.be", (await RowAsync())!.Host);
    }

    [Fact]
    public async Task DeletePut_TheDeleteAnswers204_AndNoAccountRemains()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(() => ConcurrentSaveAsync("concurrent.weesky.be")));

        Assert.IsType<NoContentResult>(await controller.DeleteSchedulingAccount(CancellationToken.None));

        Assert.Null(await RowAsync());
    }

    [Fact]
    public async Task DeleteDelete_BothAnswer204()
    {
        await StoreAsync();
        var controller = CreateController(Interleaved(ConcurrentDeleteAsync));

        Assert.IsType<NoContentResult>(await controller.DeleteSchedulingAccount(CancellationToken.None));

        Assert.Null(await RowAsync());
    }

    // ---- POST Test, with a body ----

    [Fact]
    public async Task TestEntered_ConnectsWithTheEnteredValues_AndPersistsNothing()
    {
        await StoreAsync();
        var before = (await RowAsync())!;
        var opened = OpensSucceed();

        var result = TestResult(await CreateController().TestSchedulingAccount(
            Request(host: "smtp.example.org", port: 465, security: "SslOnConnect", login: "agenda@example.org", password: "typed"),
            CancellationToken.None));

        Assert.Equal(new SchedulingAccountTestResult(true), result);
        var connection = Assert.Single(opened);
        Assert.Equal(("smtp.example.org", 465, SecureSocketOptions.SslOnConnect, "agenda@example.org"),
            (connection.SmtpHost, connection.SmtpPort, connection.SmtpSecurity, connection.Username));
        Assert.Equal("typed", Assert.IsType<PasswordCredential>(connection.Credential).Password);
        var after = (await RowAsync())!;
        Assert.Equal((before.Host, before.UpdatedAt), (after.Host, after.UpdatedAt));
        Assert.Equal(before.PasswordCipher, after.PasswordCipher);
        Assert.Null(after.LastTestAt);
        _session.Verify(s => s.DisposeAsync(), Times.Once);
        _session.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TestEntered_WithoutAPassword_UsesTheStoredOne()
    {
        await StoreAsync();
        var opened = OpensSucceed();

        TestResult(await CreateController().TestSchedulingAccount(ToStoredEndpoint(""), CancellationToken.None));

        var connection = Assert.Single(opened);
        Assert.Equal(("renamed@weesky.net", SecureSocketOptions.StartTls), (connection.Username, connection.SmtpSecurity));
        Assert.Equal(StoredPassword, Assert.IsType<PasswordCredential>(connection.Credential).Password);
    }

    [Theory]
    [InlineData("elsewhere.example", 465)]
    [InlineData("stored.weesky.be", 2525)]
    public async Task TestEntered_WithoutAPassword_OnAnotherEndpoint_IsRefused(string host, int port)
    {
        await StoreAsync();

        var result = await CreateController().TestSchedulingAccount(Request(host: host, port: port, password: ""), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("password_required_for_new_endpoint", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ASecondConcurrentTest_IsRefused_UntilTheFirstEnds()
    {
        var connecting = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Returns<MailAccountConnection, CancellationToken>(async (_, _) =>
            {
                connecting.TrySetResult();
                await release.Task;
                return Result.Success(_session.Object);
            });

        var first = CreateController().TestSchedulingAccount(Request(), CancellationToken.None);
        try
        {
            await connecting.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var second = await CreateController().TestSchedulingAccount(Request(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            var conflict = Assert.IsType<ConflictObjectResult>(second.Result);
            Assert.Equal(SchedulingAccountController.TestInProgress, Assert.IsType<ResultEnveloppe>(conflict.Value).Message);
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.Equal(new SchedulingAccountTestResult(true), TestResult(await first.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Equal(new SchedulingAccountTestResult(true), TestResult(await CreateController().TestSchedulingAccount(Request(), CancellationToken.None)));
    }

    [Fact]
    public async Task TestEntered_WithoutAPassword_WhenNoneIsStored_IsRefused()
    {
        var result = await CreateController().TestSchedulingAccount(Request(password: null), CancellationToken.None);

        Assert.Equal("password_required", Assert.IsType<ResultEnveloppe>(Assert.IsType<BadRequestObjectResult>(result.Result).Value).Message);
        Assert.Null(await RowAsync());
        _smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TestEntered_WithoutAPassword_OverAnUnreadableOne_SaysSo()
    {
        await StoreAsync(cipher: [1, 2, 3, 4]);

        var result = TestResult(await CreateController().TestSchedulingAccount(ToStoredEndpoint(""), CancellationToken.None));

        Assert.Equal(new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.PasswordUnreadable), result);
        _smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TestEntered_RefusesNone_WithoutTheCleartextOptIn_WithAStableCode()
    {
        var result = await CreateController().TestSchedulingAccount(Request(security: "None"), CancellationToken.None);

        Assert.Equal("security_none_disallowed", Assert.IsType<ResultEnveloppe>(Assert.IsType<BadRequestObjectResult>(result.Result).Value).Message);
        _smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TestEntered_RefusesAnInvalidAccount()
    {
        var result = await CreateController().TestSchedulingAccount(Request(port: 0), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _smtp.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(MailConnectionErrors.AuthenticationFailed, SchedulingAccountTestErrors.SmtpAuthFailed)]
    [InlineData(MailConnectionErrors.AuthenticationUnsupported, SchedulingAccountTestErrors.SmtpAuthUnsupported)]
    [InlineData(MailConnectionErrors.Unreachable, SchedulingAccountTestErrors.SmtpUnreachable)]
    [InlineData(MailConnectionErrors.SecureChannelFailed, SchedulingAccountTestErrors.SmtpTlsFailed)]
    [InlineData(MailConnectionErrors.TimedOut, SchedulingAccountTestErrors.SmtpTimeout)]
    [InlineData(MailConnectionErrors.NotConfigured, SchedulingAccountTestErrors.SmtpUnreachable)]
    [InlineData("anything else", SchedulingAccountTestErrors.SmtpUnreachable)]
    public async Task TestEntered_MapsEachFailure_ToAStableCode(string factoryError, string code)
    {
        OpensFail(factoryError);

        var result = TestResult(await CreateController().TestSchedulingAccount(Request(), CancellationToken.None));

        Assert.Equal(new SchedulingAccountTestResult(false, code), result);
    }

    [Fact]
    public async Task TestEntered_GivesUpAtItsOwnBudget_AsATimeout()
    {
        _clock.HoldTimers = true;
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Returns<MailAccountConnection, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return Result.Success(_session.Object);
            });

        var pending = CreateController().TestSchedulingAccount(Request(), CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => _clock.PendingTimers > 0, TimeSpan.FromSeconds(5)));
        Assert.Contains(SchedulingAccountController.TestTimeout, _clock.RequestedDelays);
        _clock.Now += SchedulingAccountController.TestTimeout;

        Assert.Equal(new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.SmtpTimeout),
            TestResult(await pending.WaitAsync(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task TestEntered_TheCallersOwnCancellation_IsNotATimeout()
    {
        var connecting = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Returns<MailAccountConnection, CancellationToken>(async (_, ct) =>
            {
                connecting.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Result.Success(_session.Object);
            });
        using var aborted = new CancellationTokenSource();

        var pending = CreateController().TestSchedulingAccount(Request(), aborted.Token);
        await connecting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await aborted.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    // ---- POST Test, without a body ----

    [Fact]
    public async Task TestStored_WithNothingStored_IsNotFound()
    {
        var result = await CreateController().TestSchedulingAccount(null, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(SchedulingAccountController.NotConfigured, Assert.IsType<ResultEnveloppe>(notFound.Value).Message);
        _smtp.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TestStored_ConnectsWithTheStoredAccount_AndRecordsTheSuccess()
    {
        await StoreAsync();
        var opened = OpensSucceed();

        var result = TestResult(await CreateController().TestSchedulingAccount(null, CancellationToken.None));

        Assert.Equal(new SchedulingAccountTestResult(true), result);
        var connection = Assert.Single(opened);
        Assert.Equal(("stored.weesky.be", 465, SecureSocketOptions.SslOnConnect, "stored@weesky.net", StoredPassword),
            (connection.SmtpHost, connection.SmtpPort, connection.SmtpSecurity, connection.Username,
                Assert.IsType<PasswordCredential>(connection.Credential).Password));
        var row = (await RowAsync())!;
        Assert.Equal(_clock.Now.UtcDateTime, row.LastTestAt);
        Assert.True(row.LastTestOk);
    }

    [Fact]
    public async Task TestStored_RecordsAFailure_WithItsCode()
    {
        await StoreAsync();
        OpensFail(MailConnectionErrors.AuthenticationFailed);

        var result = TestResult(await CreateController().TestSchedulingAccount(null, CancellationToken.None));

        Assert.Equal(new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.SmtpAuthFailed), result);
        Assert.False((await RowAsync())!.LastTestOk);
    }

    [Fact]
    public async Task TestStored_OnAnUnreadablePassword_SaysSo_AndRecordsNothing()
    {
        await StoreAsync(cipher: [1, 2, 3, 4]);

        var result = TestResult(await CreateController().TestSchedulingAccount(null, CancellationToken.None));

        Assert.Equal(new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.PasswordUnreadable), result);
        Assert.Null((await RowAsync())!.LastTestAt);
        _smtp.VerifyNoOtherCalls();
    }

    // A security value altered by hand: GET shows it as stored, and the test must not blame the password.
    [Fact]
    public async Task TestStored_OnAnUnusableSecurityValue_SaysTheStoredAccountIsInvalid()
    {
        await StoreAsync(security: "Bogus");

        var get = Assert.IsType<SchedulingAccountResponse>(
            Assert.IsType<OkObjectResult>((await CreateController().GetSchedulingAccount(CancellationToken.None)).Result).Value);
        var result = TestResult(await CreateController().TestSchedulingAccount(null, CancellationToken.None));

        Assert.True(get.PasswordReadable);
        Assert.Equal(new SchedulingAccountTestResult(false, SchedulingAccountTestErrors.StoredAccountInvalid), result);
        _smtp.VerifyNoOtherCalls();
    }
}
