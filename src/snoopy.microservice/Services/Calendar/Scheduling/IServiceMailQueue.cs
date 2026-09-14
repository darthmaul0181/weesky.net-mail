namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>Décision 10: the mails a device's write owes the invitees, sent out of any session by
/// the service account once the PUT has answered — one try, a second a minute later, then a
/// line in the log. In memory: a restart loses what waits, and only the log says so.</summary>
internal interface IServiceMailQueue
{
    /// <summary>False, and logged, when no service account is configured or the queue is full or stopped.</summary>
    bool TryEnqueue(QueuedMail mail);
}
