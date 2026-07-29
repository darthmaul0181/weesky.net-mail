using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Files a composed message under the account's drafts role folder.</summary>
public interface IDraftSaver
{
    /// <summary>Returned when no folder in the tree resolves to the drafts role.</summary>
    const string NoDraftsFolder = "no_drafts_folder";

    Task<Result<SavedDraft>> SaveAsync(
        User user, MailAccountConnection connection, SaveDraftRequest request, CancellationToken cancellationToken);
}
