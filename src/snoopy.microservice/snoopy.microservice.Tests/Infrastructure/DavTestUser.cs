namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>The one user every request of a <see cref="DavTestServer"/> authenticates as, with the
/// two switches the real handler writes as claims.</summary>
internal sealed record DavTestUser(string Email, Guid Uid, bool CardDav = true, bool CalDav = true);
