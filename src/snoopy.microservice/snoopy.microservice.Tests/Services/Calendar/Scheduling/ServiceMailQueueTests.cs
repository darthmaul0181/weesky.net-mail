using CSharpFunctionalExtensions;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Scheduling;

public class ServiceMailQueueTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private readonly Mock<ISmtpConnectionFactory> _smtp = new();
    private readonly Mock<ISmtpSession> _session = new();
    private readonly Mock<ILogger<ServiceMailQueue>> _logger = new();
    private readonly Mock<IServiceAccountProvider> _accounts = new();

    private static readonly ServiceSmtpAccount Account =
        new("mail.weesky.be", 465, SecureSocketOptions.SslOnConnect, "noreply-agenda@weesky.net", "pw");

    public ServiceMailQueueTests()
    {
        _accounts.SetupGet(a => a.IsConfigured).Returns(true);
        _accounts.Setup(a => a.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Account);
    }

    private ServiceMailQueue Create(TimeProvider? clock = null, int capacity = ServiceMailQueue.Capacity, IServiceAccountProvider? accounts = null) =>
        new(_smtp.Object, accounts ?? _accounts.Object, clock ?? TimeProvider.System, _logger.Object, capacity);

    /// <summary>A clock whose waits complete only when the test moves it: the retry is a fixed minute.</summary>
    private static MutableTimeProvider HeldClock() => new() { HoldTimers = true };

    private static QueuedMail Mail(string uid = "web-1111") => new(new MimeMessage { Subject = uid }, "Invitation " + uid);

    private void SessionOpens() =>
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(_session.Object));

    private void VerifyLogged(LogLevel level, string fragment, Times times) =>
        _logger.Verify(
            l => l.Log(level, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(fragment)),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public void TryEnqueue_RefusesWithoutAServiceAccount()
    {
        _accounts.SetupGet(a => a.IsConfigured).Returns(false);

        Assert.False(Create().TryEnqueue(Mail()));

        VerifyLogged(LogLevel.Warning, "No service account configured; invitation mail not sent: Invitation web-1111", Times.Once());
        _smtp.VerifyNoOtherCalls();
    }

    // Unknown is not "absent": the database was not read yet, and TryEnqueue never waits for it.
    [Fact]
    public void TryEnqueue_AcceptsWhileTheAccountIsStillUnknown_WithoutReadingIt()
    {
        _accounts.SetupGet(a => a.IsConfigured).Returns((bool?)null);

        Assert.True(Create().TryEnqueue(Mail()));

        _accounts.Verify(a => a.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Starting_LoadsTheAccount_SoThatEnqueueingKnowsWhetherOneExists()
    {
        var queue = Create();
        using var cts = new CancellationTokenSource(Budget);

        await queue.StartAsync(cts.Token);
        Assert.True(SpinWait.SpinUntil(() => _accounts.Invocations.Any(i => i.Method.Name == nameof(IServiceAccountProvider.GetAsync)), Budget));
        await queue.StopAsync(CancellationToken.None);
    }

    // A cancellation the stop did not cause (a database command timing out) is a failure like any other.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStartupLoadThatFails_DoesNotStopTheQueue(bool failsAsACancellation)
    {
        var sent = new TaskCompletionSource();
        _accounts.SetupSequence(a => a.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failsAsACancellation ? new TaskCanceledException("command timeout") : new InvalidOperationException("database down"))
            .ReturnsAsync(Account);
        SessionOpens();
        _session.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.TrySetResult())
            .ReturnsAsync(Result.Success());
        var queue = Create();
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail()));
        await sent.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        VerifyLogged(LogLevel.Warning, "could not be loaded", Times.Once());
    }

    [Fact]
    public void TryEnqueue_RefusesWhenTheQueueIsFull()
    {
        var queue = Create();
        var mail = Mail();
        for (var i = 0; i < ServiceMailQueue.Capacity; i++) Assert.True(queue.TryEnqueue(mail));

        Assert.False(queue.TryEnqueue(Mail("web-full")));

        VerifyLogged(LogLevel.Error, "web-full", Times.Once());
    }

    [Fact]
    public async Task Sends_WithTheStoredAccount_OnceTheHostRuns()
    {
        MailAccountConnection? opened = null;
        MimeMessage? delivered = null;
        var sent = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback<MailAccountConnection, CancellationToken>((c, _) => opened = c)
            .ReturnsAsync(Result.Success(_session.Object));
        _session.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => { delivered = m; sent.TrySetResult(); })
            .ReturnsAsync(Result.Success());
        var queue = Create();
        var mail = Mail();
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(mail));
        await sent.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        Assert.Same(mail.Message, delivered);
        Assert.Equal("noreply-agenda@weesky.net", opened!.Username);
        Assert.Equal(("mail.weesky.be", 465, SecureSocketOptions.SslOnConnect), (opened.SmtpHost, opened.SmtpPort, opened.SmtpSecurity));
        Assert.Equal("pw", Assert.IsType<PasswordCredential>(opened.Credential).Password);
        VerifyLogged(LogLevel.Information, "web-1111", Times.Once());
        _session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task TriesTwice_ThenGivesUpWithAnError()
    {
        var clock = HeldClock();
        var attempts = 0;
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref attempts))
            .ReturnsAsync(Result.Failure<ISmtpSession>("down"));
        var abandoned = new TaskCompletionSource();
        _logger.Setup(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => abandoned.TrySetResult());
        var queue = Create(clock: clock);
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail()));
        Assert.True(SpinWait.SpinUntil(() => clock.PendingTimers > 0, Budget));
        clock.Now += ServiceMailQueue.RetryDelay;
        await abandoned.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(2, attempts);
        VerifyLogged(LogLevel.Error, "web-1111", Times.Once());
    }

    [Fact]
    public async Task TheSecondTry_WaitsAMinute()
    {
        var clock = HeldClock();
        var attempts = 0;
        var second = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback(() => { if (Interlocked.Increment(ref attempts) == 2) second.TrySetResult(); })
            .ReturnsAsync(Result.Failure<ISmtpSession>("down"));
        var queue = Create(clock: clock);
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail()));
        Assert.True(SpinWait.SpinUntil(() => clock.PendingTimers > 0, Budget));
        Assert.Equal(1, attempts);
        Assert.Contains(TimeSpan.FromSeconds(60), clock.RequestedDelays);

        clock.Now += TimeSpan.FromSeconds(59);
        Assert.Equal(1, attempts);
        clock.Now += TimeSpan.FromSeconds(1);
        await second.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AFailureTheRetryQueueCannotHold_IsAbandonedAfterOneTry()
    {
        var clock = HeldClock();
        var attempts = 0;
        var abandoned = new TaskCompletionSource<string>();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref attempts))
            .ReturnsAsync(Result.Failure<ISmtpSession>("down"));
        _logger.Setup(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(i => abandoned.TrySetResult(i.Arguments[2].ToString()!)));
        var queue = Create(clock: clock, capacity: 1);
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        // A's retry is taken and waits on the held clock; B's fills the one-slot retry queue; C's finds it full.
        Assert.True(queue.TryEnqueue(Mail("web-A")));
        Assert.True(SpinWait.SpinUntil(() => clock.PendingTimers > 0, Budget));
        Assert.True(queue.TryEnqueue(Mail("web-B")));
        Assert.True(SpinWait.SpinUntil(() => attempts == 2, Budget));
        Assert.True(queue.TryEnqueue(Mail("web-C")));

        Assert.Equal("Invitation mail retry queue full; abandoned after one try: Invitation web-C", await abandoned.Task.WaitAsync(cts.Token));
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(3, attempts);
        VerifyLogged(LogLevel.Warning, "not sent: Invitation web-A", Times.Once());
        VerifyLogged(LogLevel.Warning, "not sent: Invitation web-B", Times.Once());
    }

    [Fact]
    public async Task AFailingMail_NeverHoldsTheNextOne_WhileItWaitsForItsRetry()
    {
        var firstTries = 0;
        var secondSent = new TaskCompletionSource();
        SessionOpens();
        _session.Setup(s => s.SendAsync(It.Is<MimeMessage>(m => m.Subject == "web-1111"), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref firstTries))
            .ReturnsAsync(Result.Failure("refused"));
        _session.Setup(s => s.SendAsync(It.Is<MimeMessage>(m => m.Subject == "web-2222"), It.IsAny<CancellationToken>()))
            .Callback(() => secondSent.TrySetResult())
            .ReturnsAsync(Result.Success());
        var queue = Create(clock: HeldClock());
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail("web-1111")));
        Assert.True(queue.TryEnqueue(Mail("web-2222")));
        await secondSent.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(1, firstTries);
        VerifyLogged(LogLevel.Warning, "not sent: Invitation web-1111", Times.Once());
        VerifyLogged(LogLevel.Error, "web-1111", Times.Never());
    }

    [Fact]
    public async Task Stopping_LogsTheMailInFlight_AndEveryMailStillQueued()
    {
        var started = new TaskCompletionSource();
        SessionOpens();
        _session.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Returns<MimeMessage, CancellationToken>(async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Result.Success();
            });
        var queue = Create();
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail("web-1111")));
        await started.Task.WaitAsync(cts.Token);
        Assert.True(queue.TryEnqueue(Mail("web-2222")));
        await queue.StopAsync(CancellationToken.None).WaitAsync(cts.Token);

        VerifyLogged(LogLevel.Warning, "not sent: Invitation web-1111", Times.Once());
        VerifyLogged(LogLevel.Warning, "not sent: Invitation web-2222", Times.Once());
        _session.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(queue.TryEnqueue(Mail("web-3333")));
    }

    [Fact]
    public async Task AnAccountDeletedBeforeTheSend_IsLogged_AndTheMailIsNotSent()
    {
        var clock = HeldClock();
        _accounts.Setup(a => a.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((ServiceSmtpAccount?)null);
        var abandoned = new TaskCompletionSource();
        _logger.Setup(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => abandoned.TrySetResult());
        var queue = Create(clock: clock);
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);

        Assert.True(queue.TryEnqueue(Mail()));
        Assert.True(SpinWait.SpinUntil(() => clock.PendingTimers > 0, Budget));
        clock.Now += ServiceMailQueue.RetryDelay;
        await abandoned.Task.WaitAsync(cts.Token);
        await queue.StopAsync(CancellationToken.None);

        VerifyLogged(LogLevel.Warning, "No service account configured; invitation mail not sent: Invitation web-1111", Times.Exactly(2));
        VerifyLogged(LogLevel.Error, "abandoned after two tries: Invitation web-1111", Times.Once());
        _smtp.VerifyNoOtherCalls();
    }

    /// <summary>The real provider over the real store: what Administration saves or deletes reaches the queue.</summary>
    [Fact]
    public async Task ASaveAndADelete_ReachTheQueue_ThroughInvalidation()
    {
        var database = Guid.NewGuid().ToString("N");
        var protector = new ServiceAccountSecretProtector(DataProtectionProvider.Create(nameof(ServiceMailQueueTests)));
        await using var services = new ServiceCollection()
            .AddScoped<PreferencesDbContext>(_ => new PreferencesTestDbContext(database))
            .AddScoped<ISchedulingAccountStore, SchedulingAccountStore>()
            .BuildServiceProvider();
        var accounts = new ServiceAccountProvider(
            services.GetRequiredService<IServiceScopeFactory>(), protector, NullLogger<ServiceAccountProvider>.Instance);
        MailAccountConnection? opened = null;
        var sent = new TaskCompletionSource();
        _smtp.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
            .Callback<MailAccountConnection, CancellationToken>((c, _) => opened = c)
            .ReturnsAsync(Result.Success(_session.Object));
        _session.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.TrySetResult())
            .ReturnsAsync(Result.Success());
        var queue = Create(accounts: accounts);
        using var cts = new CancellationTokenSource(Budget);
        await queue.StartAsync(cts.Token);
        Assert.True(SpinWait.SpinUntil(() => accounts.IsConfigured is not null, Budget));

        Assert.False(queue.TryEnqueue(Mail("web-before")));

        await new SchedulingAccountStore(new PreferencesTestDbContext(database)).SaveAsync(
            "smtp.weesky.be", 587, "StartTls", "agenda@weesky.net", protector.Protect("s4ved"), CancellationToken.None);
        await accounts.InvalidateAsync(CancellationToken.None);
        Assert.True(queue.TryEnqueue(Mail("web-saved")));
        await sent.Task.WaitAsync(cts.Token);
        Assert.Equal(("smtp.weesky.be", "agenda@weesky.net", "s4ved"),
            (opened!.SmtpHost, opened.Username, Assert.IsType<PasswordCredential>(opened.Credential).Password));

        await new SchedulingAccountStore(new PreferencesTestDbContext(database)).DeleteAsync(CancellationToken.None);
        await accounts.InvalidateAsync(CancellationToken.None);
        Assert.False(queue.TryEnqueue(Mail("web-deleted")));

        await queue.StopAsync(CancellationToken.None);
    }
}
