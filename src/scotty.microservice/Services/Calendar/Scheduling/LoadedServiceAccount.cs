using weesky.Scotty.Microservice.Models.Calendar;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

/// <summary>A completed load: its <c>Account</c> is null when none is usable, which is a known answer, not an unknown one.</summary>
internal sealed record LoadedServiceAccount(ServiceSmtpAccount? Account);
