using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Builds the quotable body of an original and stages the parts a reply/forward carries over.</summary>
public interface IQuotePreparer
{
    Task<Result<PreparedQuote>> PrepareAsync(string accountId, MimeMessage message, QuotePurpose purpose, CancellationToken cancellationToken);
}
