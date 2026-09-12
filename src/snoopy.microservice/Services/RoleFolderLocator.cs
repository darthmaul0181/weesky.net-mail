using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>The folder a role names on one account — the tree's flags and the user's overrides,
/// resolved the way the folder list resolves them. Null when no folder holds the role.</summary>
public interface IRoleFolderLocator
{
    Task<string?> FindAsync(User user, MailAccountConnection connection, string role, CancellationToken cancellationToken);
}

internal sealed class RoleFolderLocator(IMailFolderRepository folders, IFolderRoleStore roles) : IRoleFolderLocator
{
    public async Task<string?> FindAsync(
        User user, MailAccountConnection connection, string role, CancellationToken cancellationToken)
    {
        var tree = await folders.GetTreeAsync(user, connection, cancellationToken);
        if (tree.IsFailure) return null;
        var overrides = await roles.GetAsync(user.WebmailUid, connection.StorageAccountId, cancellationToken);
        return FolderRoleResolver.Resolve(tree.Value, overrides).Roles
            .FirstOrDefault(r => r.Role == role && r.FolderPath != null)?.FolderPath;
    }
}
