using MimeKit;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>A composed mail for the service account. <see cref="Description"/> is all the log ever
/// says of it — kind, UID, SEQUENCE, recipients — never the body (décision 10).</summary>
internal sealed record QueuedMail(MimeMessage Message, string Description);
