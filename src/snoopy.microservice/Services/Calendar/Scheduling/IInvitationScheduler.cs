using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>Décision 9's hook, called by the two doors after each resource one successful write touched.</summary>
public interface IInvitationScheduler
{
    /// <summary>
    /// Never throws; never waits on SMTP out of the user's session. <paramref name="session"/> opens the
    /// user's own SMTP session, called at most once and only when a webmail write owes a mail; null from a
    /// device. <paramref name="language"/> null reads the user's <c>ui.language</c> preference.
    /// <paramref name="cancellationToken"/> only bounds that in-session send: the write has committed, so
    /// everything it owes the invitees and the columns runs to its end.
    /// </summary>
    Task<SchedulingReport> AfterWriteAsync(User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken);
}
