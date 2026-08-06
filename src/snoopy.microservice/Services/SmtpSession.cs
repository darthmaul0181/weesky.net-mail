using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class SmtpSession : ISmtpSession
{
    /// <summary>Disposal runs after the response is produced: a QUIT round trip a half-dead peer
    /// never answers must not be what the user waits on.</summary>
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);

    private readonly SmtpClient _client;
    private readonly ILogger _logger;

    public SmtpSession(SmtpClient client, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _logger = logger;
    }

    public async Task<Result> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendAsync(message, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP refused the message");
            return Result.Failure(DescribeFailure(ex, message));
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
            {
                using var cts = new CancellationTokenSource(DisconnectTimeout);
                await _client.DisconnectAsync(quit: true, cts.Token);
            }
        }
        catch
        {
            // Best effort — the connection is being torn down anyway.
        }

        _client.Dispose();
    }

    /// <summary>
    /// A sender rejection names the address: with alias identities the likely cause is Postfix's
    /// smtpd_sender_login_maps not allowing that From, and the user must see it is a server rule.
    /// </summary>
    internal static string DescribeFailure(Exception ex, MimeMessage message)
    {
        if (ex is SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted })
        {
            var sender = message.From.Mailboxes.FirstOrDefault()?.Address;
            if (sender != null) return $"The mail server refused to send from {sender}";
        }
        return "The mail server refused the message";
    }
}
