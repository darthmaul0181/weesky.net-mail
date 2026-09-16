using CSharpFunctionalExtensions;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services.Calendar.Invitations;

public interface IInvitationReplyApplier
{
    /// <summary>Writes a guest's answer into the event the webmail invited them to. A refusal of the
    /// mail itself is the failure; a write the calendar did not take is a success carrying its code.</summary>
    Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(
        User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken cancellationToken);
}
