namespace weesky.Snoopy.Microservice.Models.Contacts;

/// One card as the protocol serves it. `VCardRaw` is the sovereign bytes; `CardHash` is their
/// SHA-256 and therefore the ETag; `UpdatedAt` is what getlastmodified renders.
public sealed record DavCard(
    Guid ContactId, string DavName, string Uid, string VCardRaw, string CardHash,
    DateTime UpdatedAt, ulong SyncSequence);
