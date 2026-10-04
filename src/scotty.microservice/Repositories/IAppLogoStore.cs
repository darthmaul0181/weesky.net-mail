namespace weesky.Scotty.Microservice.Repositories;

/// <summary>The instance logo's renditions. It checks nothing: the caller validates every image first.</summary>
public interface IAppLogoStore
{
    Task<byte[]?> GetImageAsync(int size, CancellationToken cancellationToken);
    Task<DateTime?> GetUpdatedAtAsync(CancellationToken cancellationToken);
    Task ReplaceAsync(IReadOnlyDictionary<int, byte[]> images, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
