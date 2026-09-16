using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Scotty.Microservice.Data.Preferences;

/// <summary>
/// The SMTP account that sends the invitation mails a device's write owes the guests. One row for
/// the instance, written from Administration: no user_id, and never in app_settings, which anyone
/// may read.
/// </summary>
[Table("scheduling_service_account")]
public sealed class SchedulingServiceAccount
{
    public const byte SingletonId = 1;

    [Column("id")]
    public byte Id { get; set; } = SingletonId;

    [Column("host")]
    public string Host { get; set; } = string.Empty;

    [Column("port")]
    public int Port { get; set; }

    /// <summary>None | StartTls | SslOnConnect.</summary>
    [Column("security")]
    public string Security { get; set; } = "StartTls";

    [Column("login")]
    public string Login { get; set; } = string.Empty;

    /// <summary>Data-Protection-protected. Never logged, never returned by any endpoint.</summary>
    [Column("password_cipher")]
    public byte[] PasswordCipher { get; set; } = [];

    [Column("last_test_at")]
    public DateTime? LastTestAt { get; set; }

    [Column("last_test_ok")]
    public bool? LastTestOk { get; set; }

    /// <summary>UTC. Also what a connection test checks before recording its verdict.</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
