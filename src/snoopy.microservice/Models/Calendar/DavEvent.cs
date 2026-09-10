namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>One event as the protocol serves it. <c>IcsRaw</c> is the sovereign bytes;
/// <c>IcsHash</c> is their ETag; <c>SyncSequence</c> the rank sync-collection orders by.</summary>
public sealed record DavEvent(Guid EventId, Guid CalendarId, string DavName, string Uid,
    string IcsRaw, string IcsHash, DateTime UpdatedAt, ulong SyncSequence);
