using System.Net;
using System.Net.Sockets;

namespace weesky.Scotty.Microservice.Services.Bimi;

/// <summary>
/// Resolves the host once and dials only the public addresses of that answer, so the address
/// checked is the address connected to: a second lookup would let DNS rebinding swap it.
/// </summary>
internal static class PublicOnlyConnect
{
    public static ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        ConnectAsync(context.DnsEndPoint, Dns.GetHostAddressesAsync, DialAsync, cancellationToken);

    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endPoint, Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> dial, CancellationToken cancellationToken)
    {
        var addresses = (await resolve(endPoint.Host, cancellationToken)).Where(PublicAddress.IsPublic).ToList();
        if (addresses.Count == 0) throw new HttpRequestException($"{endPoint.Host} has no public address");

        List<Exception> failures = [];
        foreach (var address in addresses)
        {
            try
            {
                return await dial(new IPEndPoint(address, endPoint.Port), cancellationToken);
            }
            catch (SocketException ex)
            {
                failures.Add(ex);
            }
        }

        throw new HttpRequestException($"{endPoint.Host} could not be reached", new AggregateException(failures));
    }

    private static async ValueTask<Stream> DialAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
