namespace weesky.Scotty.Microservice.Models;

/// <summary>
/// The calendar service account as the admin dialog enters it. <c>Security</c> is exactly one of
/// <c>None</c>, <c>StartTls</c>, <c>SslOnConnect</c>. The password is write-only: empty or absent,
/// it means the stored one.
/// </summary>
public sealed record SchedulingAccountRequest(string? Host, int Port, string? Security, string? Login, string? Password)
{
    /// <summary>Redacted: the generated ToString would print the password into any log line.</summary>
    public override string ToString() => $"SchedulingAccountRequest ({Login}, {Host}:{Port})";
}
