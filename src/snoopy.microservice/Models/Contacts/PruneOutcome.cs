namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>What one prune removed, for the sweeper's heartbeat line.</summary>
public sealed record PruneOutcome(int Tombstones, int Revisions);
