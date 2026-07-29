namespace weesky.Snoopy.Microservice.Models;

/// <summary>An admin-curated external mail provider, as the settings UI sees it.</summary>
public sealed record ExternalDomainResponse(
    Guid Id, string Name, string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity, string? SieveHost, int? SievePort);

/// <summary>
/// Registers or edits an external mail provider. <c>ImapSecurity</c>/<c>SmtpSecurity</c> must be
/// exactly one of <c>None</c>, <c>StartTls</c>, <c>SslOnConnect</c> — case-sensitive, no numeric
/// form accepted — since the resolver that later reads the stored value is case-sensitive too.
/// </summary>
public sealed record ExternalDomainRequest(
    string Name, string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity, string? SieveHost, int? SievePort);
