using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class ExternalDomainStore(PreferencesDbContext context) : IExternalDomainStore
{
    internal const string NameTaken = "A domain with this name already exists";

    internal const string NotFound = "Domain not found";

    /// <summary>Machine-readable: the caller turns it into its own message.</summary>
    internal const string InUse = "domain_in_use";

    public async Task<IReadOnlyList<ExternalDomain>> ListAsync(CancellationToken cancellationToken)
        => await context.ExternalDomains.AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

    public Task<ExternalDomain?> FindAsync(Guid id, CancellationToken cancellationToken)
        => context.ExternalDomains.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<Result<ExternalDomain>> CreateAsync(
        ExternalDomain domain, CancellationToken cancellationToken)
    {
        var name = domain.Name.Trim();
        if (await context.ExternalDomains.AnyAsync(d => d.Name == name, cancellationToken))
            return Result.Failure<ExternalDomain>(NameTaken);

        domain.Id = Guid.NewGuid();
        domain.Name = name;
        domain.CreationDate = DateTime.UtcNow;
        domain.UpdatedAt = domain.CreationDate;
        context.ExternalDomains.Add(domain);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(domain);
    }

    public async Task<Result> UpdateAsync(ExternalDomain domain, CancellationToken cancellationToken)
    {
        var existing = await context.ExternalDomains
            .FirstOrDefaultAsync(d => d.Id == domain.Id, cancellationToken);
        if (existing == null) return Result.Failure(NotFound);

        var name = domain.Name.Trim();
        if (await context.ExternalDomains.AnyAsync(
                d => d.Name == name && d.Id != domain.Id, cancellationToken))
            return Result.Failure(NameTaken);

        existing.Name = name;
        existing.ImapHost = domain.ImapHost;
        existing.ImapPort = domain.ImapPort;
        existing.ImapSecurity = domain.ImapSecurity;
        existing.SmtpHost = domain.SmtpHost;
        existing.SmtpPort = domain.SmtpPort;
        existing.SmtpSecurity = domain.SmtpSecurity;
        existing.SieveHost = domain.SieveHost;
        existing.SievePort = domain.SievePort;
        existing.AuthMode = domain.AuthMode;
        existing.OAuthAuthorizationUrl = domain.OAuthAuthorizationUrl;
        existing.OAuthTokenUrl = domain.OAuthTokenUrl;
        existing.OAuthScopes = domain.OAuthScopes;
        existing.OAuthClientId = domain.OAuthClientId;
        existing.OAuthClientSecret = domain.OAuthClientSecret;
        existing.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // Checked before the delete rather than caught after it: the FK is ON DELETE RESTRICT,
        // and a provider violation carries nothing the admin could act on.
        if (await context.ConnectedAccounts.AnyAsync(a => a.DomainId == id, cancellationToken))
            return Result.Failure(InUse);

        var existing = await context.ExternalDomains
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (existing == null) return Result.Failure(NotFound);

        context.ExternalDomains.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
