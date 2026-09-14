using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Scheduling;

/// <summary>The calendar service account as Administration stored it, read once and kept in
/// memory until the admin screen saves or deletes it.</summary>
public interface IServiceAccountProvider
{
    /// <summary>Null until a load succeeds: at startup, or after a reload that failed.</summary>
    bool? IsConfigured { get; }

    /// <summary>The cached account, loading it when unknown. Null when none is usable; throws when
    /// the database cannot be read.</summary>
    Task<ServiceSmtpAccount?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Forgets the cached account and loads the stored one again. Never throws: a failed
    /// reload leaves the state unknown, and the next <see cref="GetAsync"/> retries.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
