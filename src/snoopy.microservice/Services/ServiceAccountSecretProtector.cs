using Microsoft.AspNetCore.DataProtection;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class ServiceAccountSecretProtector(IDataProtectionProvider provider)
    : DataProtectionSecretProtector(provider, Purpose), IServiceAccountSecretProtector
{
    internal const string Purpose = "weesky.scheduling.serviceaccount.password";
}
