namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>What a request's sync-token resolved to. <see cref="Initial"/> means "the whole book, no
/// tombstones" — the canonical shape of a first sync, and also what an absent token is treated as.</summary>
internal enum SyncTokenKind
{
    Initial,
    Sequence,
    Invalid,
}
