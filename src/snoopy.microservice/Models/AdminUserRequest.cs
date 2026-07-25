using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Request body for POST and PUT /api/Admin/users.
///
/// Every optional field reads "null = keep unchanged" on PUT. They are nullable for that
/// reason alone: as plain <c>int</c>/<c>bool</c> they carried a non-null default, so a partial
/// update that simply omitted them reset the quota to 1024 and revoked admin — silently, and
/// with no way for the repository to tell "false" from "not sent". The creation defaults live
/// in <see cref="Repositories.AdminRepository"/>, applied only when the field is absent.
/// </summary>
public sealed class AdminUserRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;
    public string DomainId { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? FullName { get; set; }

    [Range(0, int.MaxValue)]
    public int? QuotaMb { get; set; }
    public bool? Active { get; set; }
    public bool? Admin { get; set; }
}
