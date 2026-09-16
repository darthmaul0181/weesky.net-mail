using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Platform;
using weesky.Scotty.Microservice.Repositories;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>The address the replies will find (décision 8): the primary account's default
/// sending identity as <see cref="IdentityResolver"/> settles it, which is the primary address
/// or a live alias — both hosted here, so the service account may send in their name.</summary>
public interface IOrganizerIdentity
{
    Task<OrganizerWrite> ResolveAsync(User user, CancellationToken cancellationToken);
}

internal sealed class OrganizerIdentity(
    ISendingIdentityStore identities, IAliasDirectory aliases, IProfileReader profiles) : IOrganizerIdentity
{
    public async Task<OrganizerWrite> ResolveAsync(User user, CancellationToken cancellationToken)
    {
        var stored = await identities.GetAsync(user.WebmailUid, AccountScope.Primary, cancellationToken);
        var owned = aliases.EnforcesOwnership ? await aliases.GetAddressesAsync(user, cancellationToken) : [];
        var chosen = IdentityResolver.Resolve(stored, user.Email, fullName: null, owned).First(i => i.IsDefault);
        // Only the primary's label depends on the account's name: an alias is named by its own row.
        var name = chosen.IsPrimary
            ? IdentityResolver.LabelFor(stored, chosen.Address, await FullNameAsync(user, cancellationToken), user.Email)
            : chosen.DisplayName;
        return new OrganizerWrite(chosen.Address, IcsComposer.CommonName(name));
    }

    private async Task<string?> FullNameAsync(User user, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetDisplayNameAsync(user, cancellationToken);
        return string.IsNullOrWhiteSpace(profile) ? user.FullName : profile;
    }
}
