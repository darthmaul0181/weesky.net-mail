using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// The two seams the principal's addresses are read through, posed as overrides on a
/// <see cref="DavTestServer"/>: the last registration wins, so a test replaces the host's default
/// without knowing how it was built.
/// </summary>
internal static class DavTestPlatform
{
    internal static IServiceCollection WithDomains(
        this IServiceCollection services, params string[] names) =>
        services.WithAccountInfo(Result.Success(new AccountInfo
        {
            Mailbox = "wsk",
            Domains = [.. names.Select(name => new Domain { Id = name[..3], Name = name })],
        }));

    /// <summary>A platform that cannot say what the account owns — the principal then answers its
    /// primary address alone.</summary>
    internal static IServiceCollection WithoutAccountInfo(this IServiceCollection services) =>
        services.WithAccountInfo(Result.Failure<AccountInfo>("the directory is unreachable"));

    internal static IServiceCollection WithSendingIdentities(
        this IServiceCollection services, params string[] addresses)
    {
        var store = new Mock<ISendingIdentityStore>();
        store.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. addresses.Select(address => new SendingIdentity { Address = address })]);
        return services.AddSingleton(store.Object);
    }

    private static IServiceCollection WithAccountInfo(
        this IServiceCollection services, Result<AccountInfo> info)
    {
        var provider = new Mock<IAccountInfoProvider>();
        provider.Setup(p => p.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(info);
        return services.AddSingleton(provider.Object);
    }
}
