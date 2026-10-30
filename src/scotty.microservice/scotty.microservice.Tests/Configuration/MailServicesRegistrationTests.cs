using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Bimi;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

/// <summary>
/// The pool is reachable through exactly one door. Resolving it, its sweeper and the scoped
/// provider proves the registrations line up; the factory being one instance under two
/// interfaces is what keeps the probes and the pool on the same certificate policy.
/// </summary>
public sealed class MailServicesRegistrationTests
{
    private static ServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptionsMonitor<MailOptions>>(new StaticOptionsMonitor(new MailOptions()));
        services.AddMailServices();
        return services;
    }

    // Resolved one by one, never IEnumerable<IHostedService>: that would construct every sweeper,
    // and the others need options this container does not carry. Disposal is asynchronous because
    // the pool and the scoped provider are IAsyncDisposable only.
    [Fact]
    public async Task ThePoolResolvesAndItsSweeperIsRegistered()
    {
        var services = Register();
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IImapConnectionPool>());
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ImapPoolSweeper));
    }

    [Fact]
    public async Task TheFactoryIsOneInstanceUnderBothInterfaces()
    {
        await using var provider = Register().BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IImapConnectionFactory>(),
            provider.GetRequiredService<IImapClientSource>());
    }

    [Fact]
    public async Task TheScopedProviderResolvesWithARequestIdentity()
    {
        await using var provider = Register().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IImapSessionProvider>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<RequestIdentity>(),
            scope.ServiceProvider.GetRequiredService<IRequestIdentity>());
    }

    [Fact]
    public async Task TheBimiClientDialsOnlyCheckedAddressesWithoutRedirectOrProxy()
    {
        await using var provider = Register().BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(BimiLogoService.ClientName);
        while (handler is DelegatingHandler { InnerHandler: { } inner }) handler = inner;
        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseProxy);
        Assert.Equal(typeof(PublicOnlyConnect).GetMethod(nameof(PublicOnlyConnect.ConnectAsync), [typeof(SocketsHttpConnectionContext), typeof(CancellationToken)]),
            sockets.ConnectCallback?.Method);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<IHttpClientFactory>().CreateClient(BimiLogoService.ClientName).Timeout);
    }

    [Fact]
    public async Task TheBimiServiceResolvesWithItsRealCollaborators()
    {
        await using var provider = Register().BuildServiceProvider();

        Assert.IsType<BimiLogoService>(provider.GetRequiredService<IBimiLogoService>());
        Assert.IsType<DnsClientTxtResolver>(provider.GetRequiredService<IDnsTxtResolver>());
        Assert.IsType<SvgSkiaRasterizer>(provider.GetRequiredService<ISvgRasterizer>());
    }

    private sealed class StaticOptionsMonitor(MailOptions value) : IOptionsMonitor<MailOptions>
    {
        public MailOptions CurrentValue => value;
        public MailOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<MailOptions, string?> listener) => null;
    }
}
