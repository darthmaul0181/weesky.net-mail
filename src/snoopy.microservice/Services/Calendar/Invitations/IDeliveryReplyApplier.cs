using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>The delivery door's entry into the reply applier: text in, outcome out, cause Delivery.
/// A REPLY's size and its mailbox are the caller's checks.</summary>
public interface IDeliveryReplyApplier
{
    Task<DeliveryReplyResponse> ApplyAsync(User user, string ics, CancellationToken cancellationToken);
}
