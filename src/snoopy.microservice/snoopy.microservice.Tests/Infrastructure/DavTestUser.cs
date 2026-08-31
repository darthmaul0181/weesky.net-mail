namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>The one user every request of a <see cref="DavTestServer"/> authenticates as.</summary>
internal sealed record DavTestUser(string Email, Guid Uid);
