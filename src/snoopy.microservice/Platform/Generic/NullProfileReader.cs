using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Platform.Generic;

/// <summary>No directory behind the mailbox: the account has no display name of its own, and a
/// From falls back to whatever the user stored against the address.</summary>
internal sealed class NullProfileReader : IProfileReader
{
    public Task<string?> GetDisplayNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
