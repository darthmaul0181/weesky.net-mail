using System.Net;
using System.Net.Sockets;

namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>A loopback listener that never answers: any connection made to it stays in its backlog.</summary>
internal sealed class ConnectionTrap : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    public ConnectionTrap() => listener.Start();

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public string Url(string path) => $"http://127.0.0.1:{Port}/{path}";

    public bool WasDialled => listener.Pending();

    public void Dispose() => listener.Stop();
}
