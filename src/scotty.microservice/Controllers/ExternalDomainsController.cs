using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>The admin-curated external providers users connect from. Core, not platform: the rows
/// live in the webmail's own database, so a generic deployment administers them too.</summary>
[Route("api/Admin/domains/external")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class ExternalDomainsController(
    IExternalDomainStore externalDomains,
    IClientSecretProtector secretProtector,
    IOptionsMonitor<MailOptions> mailOptions) : ApiBaseController
{
    /// <summary>Returns every admin-curated external mail provider a user may connect from</summary>
    /// <response code="200">Domain list</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ExternalDomainResponse>>> GetExternalDomains(
        CancellationToken cancellationToken)
    {
        var rows = await externalDomains.ListAsync(cancellationToken);
        return Ok(rows.Select(Describe).ToList());
    }

    /// <summary>Registers a new external mail provider</summary>
    /// <response code="200">Domain created</response>
    /// <response code="400">Validation error, or the name is already taken</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExternalDomainResponse>> CreateExternalDomain(
        ExternalDomainRequest request, CancellationToken cancellationToken)
    {
        var validated = Validate(request, mailOptions.CurrentValue.AllowCleartext, requireSecret: true);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var created = await externalDomains.CreateAsync(
            ToEntity(Guid.Empty, request, ProtectedSecret(request, existing: null)), cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);
        return Ok(Describe(created.Value));
    }

    /// <summary>Updates an existing external mail provider, rewriting every field</summary>
    /// <response code="204">Domain updated</response>
    /// <response code="400">Validation error, or the name is already taken</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    /// <response code="404">No such domain</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateExternalDomain(
        Guid id, ExternalDomainRequest request, CancellationToken cancellationToken)
    {
        // Read first: an empty secret on an edit means "keep the stored one" — the secret is
        // write-only, so the edit screen has nothing to send back — and validation must still
        // refuse an OAuth2 row that would end up with no secret at all.
        var existing = await externalDomains.FindAsync(id, cancellationToken);
        if (existing is null) return NotFoundEnveloppe(ExternalDomainStore.NotFound);

        var validated = Validate(
            request, mailOptions.CurrentValue.AllowCleartext,
            requireSecret: existing.OAuthClientSecret is not { Length: > 0 });
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var result = await externalDomains.UpdateAsync(
            ToEntity(id, request, ProtectedSecret(request, existing.OAuthClientSecret)), cancellationToken);
        if (result.IsFailure)
            return result.Error == ExternalDomainStore.NotFound
                ? NotFoundEnveloppe(result.Error)
                : BadRequestEnveloppe(result.Error);
        return NoContent();
    }

    /// <summary>Removes an external mail provider</summary>
    /// <response code="204">Domain deleted</response>
    /// <response code="400">The domain still has connected accounts (domain_in_use)</response>
    /// <response code="401">Unauthenticated</response>
    /// <response code="403">Not an admin</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteExternalDomain(Guid id, CancellationToken cancellationToken)
    {
        var result = await externalDomains.DeleteAsync(id, cancellationToken);
        if (result.IsFailure) return BadRequestEnveloppe(result.Error);
        return NoContent();
    }

    private static ExternalDomainResponse Describe(ExternalDomain domain) => new(
        domain.Id, domain.Name, domain.ImapHost, domain.ImapPort, domain.ImapSecurity,
        domain.SmtpHost, domain.SmtpPort, domain.SmtpSecurity, domain.SieveHost, domain.SievePort,
        domain.AuthMode, domain.OAuthAuthorizationUrl, domain.OAuthTokenUrl,
        domain.OAuthScopes, domain.OAuthClientId,
        OAuthClientSecretSet: domain.OAuthClientSecret is { Length: > 0 });

    /// <summary>A Password row carries no OAuth column at all, so a later flip back to OAuth2
    /// starts clean rather than resurrecting whatever an earlier configuration held.</summary>
    private static ExternalDomain ToEntity(Guid id, ExternalDomainRequest request, byte[]? protectedSecret)
    {
        var oauth = ParsedAuthMode(request) is MailAuthMode.OAuth2;
        return new()
        {
            Id = id,
            Name = request.Name,
            ImapHost = request.ImapHost,
            ImapPort = request.ImapPort,
            ImapSecurity = request.ImapSecurity,
            SmtpHost = request.SmtpHost,
            SmtpPort = request.SmtpPort,
            SmtpSecurity = request.SmtpSecurity,
            SieveHost = request.SieveHost,
            SievePort = request.SievePort,
            AuthMode = oauth ? MailAuthMode.OAuth2 : MailAuthMode.Password,
            OAuthAuthorizationUrl = oauth ? request.OAuthAuthorizationUrl!.Trim() : null,
            OAuthTokenUrl = oauth ? request.OAuthTokenUrl!.Trim() : null,
            OAuthScopes = oauth ? request.OAuthScopes!.Trim() : null,
            OAuthClientId = oauth ? request.OAuthClientId!.Trim() : null,
            OAuthClientSecret = oauth ? protectedSecret : null
        };
    }

    /// <summary>Null for a Password domain, the stored bytes when the edit left the field empty,
    /// the freshly protected plaintext otherwise.</summary>
    private byte[]? ProtectedSecret(ExternalDomainRequest request, byte[]? existing) =>
        ParsedAuthMode(request) is not MailAuthMode.OAuth2 ? null
        : string.IsNullOrEmpty(request.OAuthClientSecret) ? existing
        : secretProtector.Protect(request.OAuthClientSecret);

    /// <summary>Exact-literal rule, like the securities; null when unrecognised, and a null
    /// request value means Password so pre-OAuth callers keep their exact meaning.</summary>
    private static MailAuthMode? ParsedAuthMode(ExternalDomainRequest request) => request.AuthMode switch
    {
        null or "Password" => MailAuthMode.Password,
        "OAuth2" => MailAuthMode.OAuth2,
        _ => null
    };

    /// <summary>
    /// Hosts and securities follow <see cref="MailEndpointRules"/>. The cleartext opt-in is the
    /// same one the resolver applies, so a row that saves here is a row that resolves there.
    /// </summary>
    private static Result Validate(ExternalDomainRequest request, bool allowCleartext, bool requireSecret)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Result.Failure("Name must be between 1 and 100 characters");

        if (MailEndpointRules.ValidateHost(request.ImapHost) is { } imapHostError) return Result.Failure(imapHostError);
        if (MailEndpointRules.ValidateHost(request.SmtpHost) is { } smtpHostError) return Result.Failure(smtpHostError);

        if (request.ImapPort is < 1 or > 65535) return Result.Failure("Imap port must be between 1 and 65535");
        if (request.SmtpPort is < 1 or > 65535) return Result.Failure("Smtp port must be between 1 and 65535");

        if (MailEndpointRules.ValidateSecurity(request.ImapSecurity, allowCleartext) is { } imapSecurityError)
            return Result.Failure($"Imap {imapSecurityError}");
        if (MailEndpointRules.ValidateSecurity(request.SmtpSecurity, allowCleartext) is { } smtpSecurityError)
            return Result.Failure($"Smtp {smtpSecurityError}");

        if (request.SieveHost is null != request.SievePort is null)
            return Result.Failure("Sieve host and port must both be present or both be absent");
        if (request.SieveHost is not null)
        {
            if (MailEndpointRules.ValidateHost(request.SieveHost) is { } sieveHostError) return Result.Failure(sieveHostError);
            if (request.SievePort is < 1 or > 65535) return Result.Failure("Sieve port must be between 1 and 65535");
        }

        return ValidateOAuth(request, requireSecret);
    }

    /// <summary>
    /// Mirrors <see cref="OAuthProviderConfig.TryFrom"/> field for field, plus the column widths:
    /// an OAuth2 row that saves here is one the consent flow will accept, so an operator cannot
    /// store a half-configured provider and discover it at consent time.
    /// </summary>
    private static Result ValidateOAuth(ExternalDomainRequest request, bool requireSecret)
    {
        var mode = ParsedAuthMode(request);
        if (mode is null)
            return Result.Failure("Auth mode must be exactly one of Password, OAuth2");
        if (mode is MailAuthMode.Password) return Result.Success();

        if (!OAuthProviderConfig.IsHttps(request.OAuthAuthorizationUrl) || request.OAuthAuthorizationUrl!.Length > 512)
            return Result.Failure("Authorization URL must be an absolute https URL of at most 512 characters");
        if (!OAuthProviderConfig.IsHttps(request.OAuthTokenUrl) || request.OAuthTokenUrl!.Length > 512)
            return Result.Failure("Token URL must be an absolute https URL of at most 512 characters");
        if (string.IsNullOrWhiteSpace(request.OAuthScopes) || request.OAuthScopes.Length > 1024)
            return Result.Failure("Scopes must be between 1 and 1024 characters");
        if (string.IsNullOrWhiteSpace(request.OAuthClientId) || request.OAuthClientId.Length > 255)
            return Result.Failure("Client id must be between 1 and 255 characters");

        if (requireSecret && string.IsNullOrEmpty(request.OAuthClientSecret))
            return Result.Failure("A client secret is required for an OAuth2 domain");
        if (DataProtectionSecretProtector.ValidateLength(request.OAuthClientSecret, "client secret") is { } tooLong)
            return Result.Failure(tooLong);
        return Result.Success();
    }
}
