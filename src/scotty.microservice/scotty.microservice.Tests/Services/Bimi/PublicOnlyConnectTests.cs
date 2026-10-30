using System.Net;
using System.Net.Sockets;
using weesky.Scotty.Microservice.Services.Bimi;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Bimi;

public sealed class PublicOnlyConnectTests
{
    private static readonly DnsEndPoint Logo = new("logo.example", 443);
    private readonly List<IPEndPoint> dialled = [];
    private int resolutions;

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public async Task A_host_resolving_only_to_internal_addresses_is_refused_without_dialling(string address)
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync(address, "192.168.1.1").AsTask());

        Assert.Empty(dialled);
    }

    [Fact]
    public async Task A_host_resolving_to_nothing_is_refused()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync().AsTask());

        Assert.Empty(dialled);
    }

    [Fact]
    public async Task Mixed_answers_dial_the_public_address_only()
    {
        await using var stream = await ConnectAsync("10.0.0.1", "93.184.216.34", "::1");

        Assert.Equal([new IPEndPoint(IPAddress.Parse("93.184.216.34"), 443)], dialled);
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task A_failed_public_address_falls_through_to_the_next_public_one_never_to_an_internal_one()
    {
        await using var stream = await ConnectAsync(failing: "93.184.216.34", "93.184.216.34", "127.0.0.1", "2606:4700::1");

        Assert.Equal(["93.184.216.34", "2606:4700::1"], dialled.Select(e => e.Address.ToString()));
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public async Task Every_public_address_failing_surfaces_as_a_request_failure()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync(failing: "93.184.216.34", "93.184.216.34").AsTask());
    }

    [Fact]
    public async Task The_real_callback_never_opens_a_socket_to_a_loopback_literal()
    {
        using var trap = new ConnectionTrap();
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = PublicOnlyConnect.ConnectAsync, UseProxy = false });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(trap.Url("logo.svg")));

        Assert.False(trap.WasDialled);
    }

    [Fact]
    public async Task The_real_callback_refuses_localhost()
    {
        using var trap = new ConnectionTrap();
        using var client = new HttpClient(new SocketsHttpHandler { ConnectCallback = PublicOnlyConnect.ConnectAsync, UseProxy = false });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://localhost:{trap.Port}/logo.svg"));

        Assert.False(trap.WasDialled);
    }

    private ValueTask<Stream> ConnectAsync(params string[] answers) => ConnectAsync(failing: null, answers);

    private ValueTask<Stream> ConnectAsync(string? failing, params string[] answers) =>
        PublicOnlyConnect.ConnectAsync(
            Logo,
            (host, _) =>
            {
                Assert.Equal(Logo.Host, host);
                resolutions++;
                return Task.FromResult(answers.Select(IPAddress.Parse).ToArray());
            },
            (endPoint, _) =>
            {
                dialled.Add(endPoint);
                return endPoint.Address.ToString() == failing
                    ? ValueTask.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused))
                    : ValueTask.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None);
}
