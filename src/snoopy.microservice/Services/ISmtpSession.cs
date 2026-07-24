using CSharpFunctionalExtensions;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

public interface ISmtpSession : IAsyncDisposable
{
    /// <summary>Submits one message. The envelope is derived from the message's To/Cc/Bcc.</summary>
    Task<Result> SendAsync(MimeMessage message, CancellationToken cancellationToken);
}
