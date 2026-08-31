namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One batch of the 4a backfill: how many contacts it gave a card, a hash and a projection to,
/// and how many are still waiting. The caller calls again while <c>Remaining</c> is above zero.
/// </summary>
public sealed record BackfillOutcome(int Processed, int Remaining);
