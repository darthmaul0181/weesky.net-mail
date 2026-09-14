namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// The calendar service account as Administration shows it. The password has no field here:
/// <c>PasswordStored</c> and <c>PasswordReadable</c> are all a reader learns. The endpoint fields
/// and the last test are null — so absent from the JSON — when there is nothing to report.
/// <c>AllowCleartext</c> mirrors Mail:AllowCleartext: whether <c>None</c> can be saved at all.
/// </summary>
public sealed record SchedulingAccountResponse(
    bool Configured, string? Host, int? Port, string? Security, string? Login,
    bool PasswordStored, bool PasswordReadable, bool AllowCleartext, DateTime? LastTestAt, bool? LastTestOk)
{
    internal static SchedulingAccountResponse None(bool allowCleartext) => new(false, null, null, null, null, false, false, allowCleartext, null, null);
}
