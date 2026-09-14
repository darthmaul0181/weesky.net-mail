using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>The hook as <see cref="DavTestServer"/> carries it: no mail, no store, only what each
/// door handed over. A singleton, so the calls outlive the request scopes that made them.</summary>
internal sealed class RecordingInvitationScheduler : IInvitationScheduler
{
    internal sealed record Call(EventChange Change, WriteOrigin Origin, bool HasSession, string? Language);

    public List<Call> Calls { get; } = [];

    public Task<SchedulingReport> AfterWriteAsync(User user, EventChange change, WriteOrigin origin,
        Func<CancellationToken, Task<MailAccountConnection?>>? session, string? language, CancellationToken cancellationToken)
    {
        lock (Calls) Calls.Add(new Call(change, origin, session is not null, language));
        return Task.FromResult(new SchedulingReport(null, 0));
    }
}
