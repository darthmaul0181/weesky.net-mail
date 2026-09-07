namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>A name that disappeared and the rank at which it did — the one shape both protocols'
/// tombstone rows project to, so sync-collection reads a single kind of deletion.</summary>
internal sealed record DavTombstone(string DavName, ulong Rank);
