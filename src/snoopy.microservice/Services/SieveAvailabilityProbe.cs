using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Connects, reads capability lines up to the greeting's OK/NO/BYE, and disconnects — the same
/// framing <see cref="ManageSieveClient"/> reads before it ever writes AUTHENTICATE. Registered as
/// a singleton so the per-(host, port) cache lives for the whole process; a failed probe (including
/// one that timed out) is cached exactly like a successful one; both only change on redeploy.
/// </summary>
internal sealed class SieveAvailabilityProbe(
    IOptions<SieveOptions> options, ILogger<SieveAvailabilityProbe> logger) : ISieveAvailabilityProbe
{
    // Its own ceiling, deliberately not SieveOptions.TimeoutSeconds outright: this backs a UI
    // flag, not a user action waiting on a real session, so it must never hold a request open as
    // long as an actual ManageSieve round trip is allowed to.
    private static readonly TimeSpan MaxProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<(string Host, int Port), Lazy<Task<bool>>> _cache = new();

    public Task<bool> IsAvailableAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host)) return Task.FromResult(false);

        // Lazy<> makes the factory run exactly once per key even under concurrent first callers;
        // WaitAsync lets a caller's own cancellation stop waiting without cancelling the shared
        // probe that every other caller — present and future — reads from the same cache entry.
        var probe = _cache.GetOrAdd((host, port), key => new Lazy<Task<bool>>(() => ProbeAsync(key.Host, key.Port)));
        return probe.Value.WaitAsync(cancellationToken);
    }

    private async Task<bool> ProbeAsync(string host, int port)
    {
        var timeoutSeconds = Math.Min(options.Value.TimeoutSeconds, (int)MaxProbeTimeout.TotalSeconds);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        TcpClient? tcp = null;
        ManageSieveWire? wire = null;
        try
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token);

            wire = new ManageSieveWire(tcp.GetStream(), tcp);
            tcp = null; // the wire owns the socket from here

            while (true)
            {
                var line = await wire.ReadLineAsync(cts.Token);
                if (line == null) return false;
                if (ManageSieveWire.TryParseStatus(line, out var status)) return status.IsOk;
            }
        }
        catch (Exception ex)
        {
            // Timeout, refused connection, TCP reset — all of them mean "not available", never a
            // fault propagated into the shared cached Task, which must stay usable forever.
            logger.LogWarning(ex, "ManageSieve availability probe failed for {Host}:{Port}", host, port);
            return false;
        }
        finally
        {
            if (wire != null) await wire.DisposeAsync();
            tcp?.Dispose();
        }
    }
}
