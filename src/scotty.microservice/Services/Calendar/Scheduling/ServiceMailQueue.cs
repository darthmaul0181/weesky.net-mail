using System.Threading.Channels;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>
/// Two readers, so a failing SMTP never holds the queue: the first tries in arrival order, and the
/// second tries, each waiting <see cref="RetryDelay"/> after its first. A mail the stop catches — in
/// flight, queued or waiting — is lost with a line in the log, as décision 10 accepts.
/// </summary>
internal sealed class ServiceMailQueue(
    ISmtpConnectionFactory smtp, IServiceAccountProvider accounts, TimeProvider clock, ILogger<ServiceMailQueue> logger,
    int capacity = ServiceMailQueue.Capacity)
    : BackgroundService, IServiceMailQueue
{
    internal const int Capacity = 1000;

    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);

    // FullMode Wait: the only mode in which TryWrite answers false on a full channel instead of dropping.
    private readonly Channel<QueuedMail> firsts = Channel.CreateBounded<QueuedMail>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly Channel<(QueuedMail Mail, DateTimeOffset Due)> retries = Channel.CreateBounded<(QueuedMail, DateTimeOffset)>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    /// <summary>Refuses only an account known to be absent. Until the first load answers, the mail is
    /// taken and the sender finds out — enqueueing never waits on the database.</summary>
    public bool TryEnqueue(QueuedMail mail)
    {
        if (accounts.IsConfigured is false)
        {
            logger.LogWarning("No service account configured; invitation mail not sent: {Description}", mail.Description);
            return false;
        }
        if (firsts.Writer.TryWrite(mail)) return true;
        logger.LogError("Invitation mail queue full or stopped; not sent: {Description}", mail.Description);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await LoadAccountAsync(stoppingToken);
            await Task.WhenAll(FirstTriesAsync(stoppingToken), SecondTriesAsync(stoppingToken));
        }
        finally
        {
            firsts.Writer.TryComplete();
            retries.Writer.TryComplete();
            while (firsts.Reader.TryRead(out var mail)) Dropped(mail);
            while (retries.Reader.TryRead(out var retry)) Dropped(retry.Mail);
        }
    }

    private async Task LoadAccountAsync(CancellationToken stoppingToken)
    {
        try
        {
            await accounts.GetAsync(stoppingToken);
        }
        // Only the stop's own cancellation ends the queue; a load failing as the host stops is still just logged.
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The calendar service account could not be loaded at startup; the first invitation mail reads it again");
        }
    }

    private async Task FirstTriesAsync(CancellationToken stoppingToken)
    {
        await foreach (var mail in firsts.Reader.ReadAllAsync(stoppingToken))
        {
            if (await TrySendAsync(mail, due: null, stoppingToken)) continue;
            var due = clock.GetUtcNow() + RetryDelay;
            if (!retries.Writer.TryWrite((mail, due)))
                logger.LogError("Invitation mail retry queue full; abandoned after one try: {Description}", mail.Description);
        }
    }

    private async Task SecondTriesAsync(CancellationToken stoppingToken)
    {
        await foreach (var (mail, due) in retries.Reader.ReadAllAsync(stoppingToken))
        {
            if (!await TrySendAsync(mail, due, stoppingToken))
                logger.LogError("Invitation mail abandoned after two tries: {Description}", mail.Description);
        }
    }

    private async Task<bool> TrySendAsync(QueuedMail mail, DateTimeOffset? due, CancellationToken stoppingToken)
    {
        try
        {
            if (due - clock.GetUtcNow() is { } wait && wait > TimeSpan.Zero) await Task.Delay(wait, clock, stoppingToken);

            if (await accounts.GetAsync(stoppingToken) is not { } account)
            {
                logger.LogWarning("No service account configured; invitation mail not sent: {Description}", mail.Description);
                return false;
            }
            var opened = await smtp.OpenAsync(account.ToConnection(), stoppingToken);
            if (opened.IsFailure)
            {
                logger.LogWarning("Service SMTP unavailable ({Error}): {Description}", opened.Error, mail.Description);
                return false;
            }
            await using var session = opened.Value;
            var sent = await session.SendAsync(mail.Message, stoppingToken);
            if (sent.IsFailure)
            {
                logger.LogWarning("Service SMTP refused ({Error}): {Description}", sent.Error, mail.Description);
                return false;
            }
            logger.LogInformation("Invitation mail sent by the service account: {Description}", mail.Description);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Dropped(mail);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Service mail try threw (account read or SMTP): {Description}", mail.Description);
            return false;
        }
    }

    private void Dropped(QueuedMail mail) =>
        logger.LogWarning("Service mail queue stopping; invitation mail not sent: {Description}", mail.Description);
}
