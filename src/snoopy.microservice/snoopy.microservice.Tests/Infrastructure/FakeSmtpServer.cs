using System.Net;
using System.Net.Sockets;
using System.Text;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// Just enough ESMTP to take an SmtpClient through Connect and Authenticate, advertising exactly
/// the <c>EHLO</c> extensions a test names — so a server without STARTTLS or without AUTH is one
/// constructor argument away. Every AUTH is accepted; one connection, then it stops.
/// </summary>
internal sealed class FakeSmtpServer(params string[] extensions) : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public void Start()
    {
        listener.Start();
        _ = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync(stop.Token) is { } line)
            {
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                        string[] lines = ["fake", .. extensions];
                        for (var i = 0; i < lines.Length; i++)
                            await writer.WriteLineAsync($"250{(i < lines.Length - 1 ? '-' : ' ')}{lines[i]}");
                        break;
                    case "AUTH":
                        await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
    }
}
