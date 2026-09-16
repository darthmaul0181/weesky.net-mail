using Microsoft.AspNetCore.DataProtection;

namespace weesky.Scotty.Microservice.Services;

internal sealed class ClientSecretProtector(IDataProtectionProvider provider)
    : DataProtectionSecretProtector(provider, Purpose), IClientSecretProtector
{
    internal const string Purpose = "weesky.oauth.clientsecret";
}
