using System.Text;
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// ManageSieve (RFC 5804) protocol session built on top of an already-connected and
/// authenticated <see cref="ManageSieveWire"/>. The transport (TCP/TLS) and the SASL handshake
/// are handled by <see cref="ManageSieveClient"/>; this class only speaks the
/// post-authentication command set.
///
/// It owns the wire it is handed: disposing the session closes the connection.
/// </summary>
internal sealed class ManageSieveSession : IManageSieveSession
{
    private const string ControlCharacterInName = "Script name contains a forbidden control character";

    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The socket is closing either way: a LOGOUT the peer never reads must not hold the
    /// response back, since disposal is the request's last act.</summary>
    private static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(2);

    private readonly ManageSieveWire _wire;
    private readonly TimeSpan _operationTimeout;
    private bool _disposed;

    public ManageSieveSession(ManageSieveWire wire, TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(wire);
        _wire = wire;
        _operationTimeout = operationTimeout is { } timeout && timeout > TimeSpan.Zero ? timeout : DefaultOperationTimeout;
    }

    public Task<Result<IReadOnlyList<SieveScriptListEntry>>> ListScriptsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return BoundedAsync(cancellationToken, ListScriptsCoreAsync);
    }

    private async Task<Result<IReadOnlyList<SieveScriptListEntry>>> ListScriptsCoreAsync(CancellationToken cancellationToken)
    {
        await _wire.WriteLineAsync("LISTSCRIPTS", cancellationToken);

        var entries = new List<SieveScriptListEntry>();
        while (true)
        {
            var line = await _wire.ReadLineAsync(cancellationToken);
            if (line == null)
                return Result.Failure<IReadOnlyList<SieveScriptListEntry>>(_wire.ReadFailure);
            if (ManageSieveWire.TryParseStatus(line, out var status))
            {
                return status.IsOk
                    ? Result.Success<IReadOnlyList<SieveScriptListEntry>>(entries)
                    : Result.Failure<IReadOnlyList<SieveScriptListEntry>>(status.Message ?? "Unable to list Sieve scripts");
            }

            var entry = await ParseListEntryAsync(line, cancellationToken);
            if (entry.IsFailure)
                return Result.Failure<IReadOnlyList<SieveScriptListEntry>>(entry.Error);
            if (entry.Value != null) entries.Add(entry.Value);
        }
    }

    public Task<Result<string>> GetScriptAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(Result.Failure<string>("Script name is required"));

        var quoted = QuoteName(name);
        if (quoted.IsFailure) return Task.FromResult(Result.Failure<string>(quoted.Error));

        return BoundedAsync(cancellationToken, token => GetScriptCoreAsync(quoted.Value, token));
    }

    private async Task<Result<string>> GetScriptCoreAsync(string quotedName, CancellationToken cancellationToken)
    {
        await _wire.WriteLineAsync($"GETSCRIPT {quotedName}", cancellationToken);

        var first = await _wire.ReadLineAsync(cancellationToken);
        if (first == null)
            return Result.Failure<string>(_wire.ReadFailure);

        if (ManageSieveWire.TryParseStatus(first, out var status))
            return status.IsOk
                ? Result.Success(string.Empty)
                : Result.Failure<string>(status.Message ?? "Script not found");

        // Expect a literal payload: {N} or {N+}
        if (!ManageSieveWire.TryParseLiteralPrefix(first, out var size))
            return Result.Failure<string>("Unexpected response from server");

        var payload = await _wire.ReadExactlyAsync(size, cancellationToken);
        if (payload.IsFailure)
            return Result.Failure<string>(payload.Error);

        // After the literal, the server sends a CRLF then the final OK/NO line.
        // We consume the CRLF by reading one (possibly empty) line.
        await _wire.ReadLineAsync(cancellationToken);
        var final = await ReadTerminatorAsync(cancellationToken);
        if (!final.IsOk)
            return Result.Failure<string>(final.Message ?? "Unable to fetch script");

        return Result.Success(ManageSieveWire.Utf8.GetString(payload.Value));
    }

    public Task<Result> PutScriptAsync(string name, string content, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(Result.Failure("Script name is required"));

        var quoted = QuoteName(name);
        if (quoted.IsFailure) return Task.FromResult(Result.Failure(quoted.Error));

        return BoundedAsync(cancellationToken, token => PutScriptCoreAsync(quoted.Value, content ?? string.Empty, token));
    }

    private async Task<Result> PutScriptCoreAsync(string quotedName, string content, CancellationToken cancellationToken)
    {
        await _wire.WriteLiteralCommandAsync($"PUTSCRIPT {quotedName}", ManageSieveWire.Utf8.GetBytes(content), cancellationToken);

        var status = await ReadTerminatorAsync(cancellationToken);
        return status.IsOk
            ? Result.Success()
            : Result.Failure(status.Message ?? "Unable to save script");
    }

    public Task<Result> SetActiveAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var quoted = QuoteName(name ?? string.Empty);
        if (quoted.IsFailure) return Task.FromResult(Result.Failure(quoted.Error));

        return BoundedAsync(cancellationToken,
            token => SendSimpleAsync($"SETACTIVE {quoted.Value}", "Unable to change active script", token));
    }

    public Task<Result> DeleteScriptAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(Result.Failure("Script name is required"));

        var quoted = QuoteName(name);
        if (quoted.IsFailure) return Task.FromResult(Result.Failure(quoted.Error));

        return BoundedAsync(cancellationToken,
            token => SendSimpleAsync($"DELETESCRIPT {quoted.Value}", "Unable to delete script", token));
    }

    private async Task<Result> SendSimpleAsync(string command, string failureFallback, CancellationToken cancellationToken)
    {
        await _wire.WriteLineAsync(command, cancellationToken);
        var status = await ReadTerminatorAsync(cancellationToken);
        return status.IsOk
            ? Result.Success()
            : Result.Failure(status.Message ?? failureFallback);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        using (var logoutCts = new CancellationTokenSource(LogoutTimeout))
        {
            try
            {
                await _wire.WriteLineAsync("LOGOUT", logoutCts.Token);
            }
            catch
            {
                // best-effort
            }
        }
        await _wire.DisposeAsync();
    }

    // ---------- Protocol helpers ----------

    /// <summary>
    /// Every read and write below is async, so the socket-level ReceiveTimeout/SendTimeout the
    /// transport sets bind none of them: this is the only thing keeping a silent peer from holding
    /// the request open. A timeout the caller did not ask for is a failure, never an exception.
    /// </summary>
    private async Task<Result<T>> BoundedAsync<T>(CancellationToken cancellationToken, Func<CancellationToken, Task<Result<T>>> operation)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_operationTimeout);
        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<T>(SieveErrors.TimedOut);
        }
    }

    private async Task<Result> BoundedAsync(CancellationToken cancellationToken, Func<CancellationToken, Task<Result>> operation)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_operationTimeout);
        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(SieveErrors.TimedOut);
        }
    }

    private async Task<ManageSieveWire.Status> ReadTerminatorAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _wire.ReadLineAsync(cancellationToken);
            if (line == null) return new ManageSieveWire.Status(false, _wire.ReadFailure);

            if (ManageSieveWire.TryParseStatus(line, out var status)) return status;
        }
    }

    /// <summary>
    /// The single path from a caller-supplied script name to the wire. ManageSieve is line
    /// oriented, so a name carrying CR/LF would split the command and inject another one;
    /// returning a <see cref="Result{T}"/> is what stops a new verb from quoting unchecked.
    /// </summary>
    private static Result<string> QuoteName(string name)
    {
        var sb = new StringBuilder(name.Length + 2);
        sb.Append('"');
        foreach (var c in name)
        {
            if (char.IsControl(c)) return Result.Failure<string>(ControlCharacterInName);
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('"');
        return Result.Success(sb.ToString());
    }

    /// <summary>A null value is a line this parser does not recognise and skips; a failure is the
    /// stream itself giving up, which the listing must not survive.</summary>
    private async Task<Result<SieveScriptListEntry?>> ParseListEntryAsync(string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(line)) return Result.Success<SieveScriptListEntry?>(null);

        // Quoted form: "name" [ACTIVE]
        if (line[0] == '"')
        {
            int end = -1;
            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length) { i++; continue; }
                if (line[i] == '"') { end = i; break; }
            }
            if (end < 0) return Result.Success<SieveScriptListEntry?>(null);
            var name = ManageSieveWire.Unquote(line[..(end + 1)]) ?? string.Empty;
            var rest = line[(end + 1)..].Trim();
            return Result.Success<SieveScriptListEntry?>(new SieveScriptListEntry(name, IsActiveKeyword(rest)));
        }

        // Literal form: {N} or {N+} on its own line, then N bytes of name, then maybe " ACTIVE\r\n"
        if (line[0] == '{' && ManageSieveWire.TryParseLiteralPrefix(line, out var size))
        {
            var nameBytes = await _wire.ReadExactlyAsync(size, cancellationToken);
            if (nameBytes.IsFailure) return Result.Failure<SieveScriptListEntry?>(nameBytes.Error);

            var name = ManageSieveWire.Utf8.GetString(nameBytes.Value);
            var trailing = await _wire.ReadLineAsync(cancellationToken);
            return Result.Success<SieveScriptListEntry?>(
                new SieveScriptListEntry(name, trailing != null && IsActiveKeyword(trailing.Trim())));
        }

        return Result.Success<SieveScriptListEntry?>(null);
    }

    private static bool IsActiveKeyword(string s) =>
        s.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ManageSieveSession));
    }
}
