using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Builds the quotable body of an original and stages the parts a reply/forward carries over.</summary>
public interface IQuotePreparer
{
    Task<Result<PreparedQuote>> PrepareAsync(string stagedScope, MimeMessage message, QuotePurpose purpose, CancellationToken cancellationToken);
}
