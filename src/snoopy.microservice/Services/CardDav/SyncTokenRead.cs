namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>One reading of a client's sync-token: what it meant, and the rank to resume from when
/// it meant one.</summary>
internal sealed record SyncTokenRead(SyncTokenKind Kind, ulong Sequence);
