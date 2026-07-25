namespace weesky.Snoopy.Microservice.Repositories;

public interface IWebmailUserStore
{
    /// <summary>
    /// Ensures the account's row exists and stamps the login. Called once per login, never per
    /// request. Returns the stable GUID (created if absent). Email is canonicalised.
    /// </summary>
    Task<Guid> RegisterLoginAsync(string email, CancellationToken cancellationToken);

    /// <summary>Removes the account's row if present (0 rows = success). The FK cascade removes preferences.</summary>
    Task DeleteByEmailAsync(string email, CancellationToken cancellationToken);
}
