using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A scripted IMAP server that accepts any number of connections and counts what each one sends, so
/// pool tests assert on the wire rather than on mocks. <see cref="SilenceOpenConnections"/> turns
/// every connection open at that moment into a black hole: read, never answered, socket still up.
/// </summary>
internal sealed class PoolImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly object _gate = new();
    private readonly List<string> _commands = [];
    private readonly List<StrongBox<bool>> _silence = [];
    private int _logins, _logouts, _noops, _closes, _expunges, _open;
    private volatile bool _refuseNoop;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Logins => Volatile.Read(ref _logins);
    public int Logouts => Volatile.Read(ref _logouts);
    public int NoOps => Volatile.Read(ref _noops);
    public int Closes => Volatile.Read(ref _closes);
    public int Expunges => Volatile.Read(ref _expunges);

    /// <summary>Connections accepted and not yet closed by either side.</summary>
    public int Open => Volatile.Read(ref _open);

    public IReadOnlyList<string> Commands
    {
        get { lock (_gate) return _commands.ToArray(); }
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public void SilenceOpenConnections()
    {
        lock (_gate) foreach (var box in _silence) box.Value = true;
    }

    /// <summary>Answers BAD to every subsequent NOOP: the socket fails health while staying live,
    /// unlike a silenced one, so what the pool does with it is visible on the wire.</summary>
    public void RefuseNoop() => _refuseNoop = true;

    /// <summary>Polls until <paramref name="predicate"/> holds or the timeout passes; true when it held.</summary>
    public async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true) _ = ServeAsync(await _listener.AcceptTcpClientAsync());
        }
        catch (Exception)
        {
            // Listener stopped: the test is over.
        }
    }

    private async Task ServeAsync(TcpClient tcpClient)
    {
        var silent = new StrongBox<bool>(false);
        lock (_gate) _silence.Add(silent);
        Interlocked.Increment(ref _open);

        try
        {
            using (tcpClient)
            using (var stream = tcpClient.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            await using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
            {
                await writer.WriteLineAsync($"* OK [CAPABILITY {Caps}] Pool fake ready");

                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null) return;
                    lock (_gate) _commands.Add(line);

                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2) continue;
                    var tag = words[0];
                    var command = words[1].ToUpperInvariant();
                    if (command == "UID" && words.Length > 2) command = "UID " + words[2].ToUpperInvariant();

                    if (silent.Value) continue;

                    switch (command)
                    {
                        case "LOGIN":
                            Interlocked.Increment(ref _logins);
                            await writer.WriteLineAsync($"{tag} OK [CAPABILITY {Caps}] LOGIN completed");
                            break;

                        case "CAPABILITY":
                            await writer.WriteLineAsync($"* CAPABILITY {Caps}");
                            await writer.WriteLineAsync($"{tag} OK CAPABILITY completed");
                            break;

                        case "NAMESPACE":
                            await writer.WriteLineAsync("* NAMESPACE ((\"\" \"/\")) NIL NIL");
                            await writer.WriteLineAsync($"{tag} OK NAMESPACE completed");
                            break;

                        case "NOOP":
                            Interlocked.Increment(ref _noops);
                            await writer.WriteLineAsync(_refuseNoop
                                ? $"{tag} BAD NOOP refused"
                                : $"{tag} OK NOOP completed");
                            break;

                        case "LIST":
                            await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                            await writer.WriteLineAsync($"{tag} OK LIST completed");
                            break;

                        case "SELECT":
                            await writer.WriteLineAsync("* 1 EXISTS");
                            await writer.WriteLineAsync("* 0 RECENT");
                            await writer.WriteLineAsync("* FLAGS (\\Seen \\Flagged \\Deleted)");
                            await writer.WriteLineAsync("* OK [PERMANENTFLAGS (\\Seen \\Flagged \\Deleted)] Flags");
                            await writer.WriteLineAsync("* OK [UIDVALIDITY 1] UIDs valid");
                            await writer.WriteLineAsync("* OK [UIDNEXT 2] Predicted next UID");
                            await writer.WriteLineAsync($"{tag} OK [READ-WRITE] SELECT completed");
                            break;

                        case "UID STORE":
                            await writer.WriteLineAsync($"{tag} OK STORE completed");
                            break;

                        case "CLOSE":
                            Interlocked.Increment(ref _closes);
                            await writer.WriteLineAsync($"{tag} OK CLOSE completed");
                            break;

                        case "EXPUNGE":
                            Interlocked.Increment(ref _expunges);
                            await writer.WriteLineAsync($"{tag} OK EXPUNGE completed");
                            break;

                        case "LOGOUT":
                            Interlocked.Increment(ref _logouts);
                            await writer.WriteLineAsync("* BYE logging out");
                            await writer.WriteLineAsync($"{tag} OK LOGOUT completed");
                            return;

                        default:
                            await writer.WriteLineAsync($"{tag} BAD unhandled command in fake server: {command}");
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Torn down by the client or by the test: the assertions are the source of truth.
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    public void Dispose() => _listener.Stop();
}
