using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class SmtpSession : ISmtpSession
{
    private readonly SmtpClient _client;
    private readonly ILogger _logger;

    public SmtpSession(SmtpClient client, ILogger logger)
    {
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
        try { await _client.DisconnectAsync(quit: true); } catch { /* connection already gone */ }
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
