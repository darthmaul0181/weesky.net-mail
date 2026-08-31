namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>What one prune removed, for the sweeper's heartbeat line.</summary>
/// <param name="Tombstones">tombstones removed by this pass</param>
/// <param name="Revisions">revisions removed by this pass</param>
/// <param name="Capped">
/// True when the pass hit its per-table ceiling, so older rows are still waiting. Reported rather
/// than left to be inferred from the counts: a bounded sweep that says nothing reads exactly like
/// a complete one, and the difference is whether an operator should look at why the backlog grew.
/// </param>
public sealed record PruneOutcome(int Tombstones, int Revisions, bool Capped = false);
