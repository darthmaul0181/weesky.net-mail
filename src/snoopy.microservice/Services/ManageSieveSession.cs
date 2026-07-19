using System.Text;
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// ManageSieve (RFC 5804) protocol session built on top of an already-connected and
/// authenticated <see cref="Stream"/>. The transport (TCP/TLS) and the SASL handshake
/// are handled by <see cref="ManageSieveClient"/>; this class only speaks the
/// post-authentication command set.
/// </summary>
internal sealed class ManageSieveSession : IManageSieveSession
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly byte[] CrLf = { 0x0D, 0x0A };

    private readonly Stream _stream;
    private readonly Func<ValueTask>? _onDisposeAsync;
    private readonly byte[] _readBuffer = new byte[8192];
    private int _readBufferLen;
    private int _readBufferPos;
    private bool _disposed;

    public ManageSieveSession(Stream stream, Func<ValueTask>? onDisposeAsync = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _onDisposeAsync = onDisposeAsync;
    }

    public async Task<Result<IReadOnlyList<SieveScriptListEntry>>> ListScriptsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await SendLineAsync("LISTSCRIPTS", cancellationToken);

        var entries = new List<SieveScriptListEntry>();
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (line == null)
                return Result.Failure<IReadOnlyList<SieveScriptListEntry>>("Connection closed");
            if (IsTerminator(line, out var status))
            {
                return status.IsOk
                    ? Result.Success<IReadOnlyList<SieveScriptListEntry>>(entries)
                    : Result.Failure<IReadOnlyList<SieveScriptListEntry>>(status.Message ?? "Unable to list Sieve scripts");
            }

            var entry = await ParseListEntryAsync(line, cancellationToken);
            if (entry != null) entries.Add(entry);
        }
    }

    public async Task<Result<string>> GetScriptAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
            return Result.Failure<string>("Script name is required");

        await SendLineAsync($"GETSCRIPT {QuoteString(name)}", cancellationToken);

        var first = await ReadLineAsync(cancellationToken);
        if (first == null)
            return Result.Failure<string>("Connection closed");

        if (IsTerminator(first, out var status))
            return status.IsOk
                ? Result.Success(string.Empty)
                : Result.Failure<string>(status.Message ?? "Script not found");

        // Expect a literal payload: {N} or {N+}
        if (!TryParseLiteralPrefix(first, out var size))
            return Result.Failure<string>("Unexpected response from server");

        var bytes = await ReadExactlyAsync(size, cancellationToken);
        // After the literal, the server sends a CRLF then the final OK/NO line.
        // We consume the CRLF by reading one (possibly empty) line.
        await ReadLineAsync(cancellationToken);
        var dataLines = new List<string>();
        var final = await ReadTerminatorAsync(dataLines, cancellationToken);
        if (!final.IsOk)
            return Result.Failure<string>(final.Message ?? "Unable to fetch script");

        return Result.Success(Utf8.GetString(bytes));
    }

    public async Task<Result> PutScriptAsync(string name, string content, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
            return Result.Failure("Script name is required");
        content ??= string.Empty;

        var contentBytes = Utf8.GetBytes(content);
        var header = Utf8.GetBytes($"PUTSCRIPT {QuoteString(name)} {{{contentBytes.Length}+}}\r\n");

        await _stream.WriteAsync(header, cancellationToken);
        await _stream.WriteAsync(contentBytes, cancellationToken);
        await _stream.WriteAsync(CrLf, cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        var response = await ReadResponseAsync(cancellationToken);
        return response.IsOk
            ? Result.Success()
            : Result.Failure(response.Message ?? "Unable to save script");
    }

    public Task<Result> SetActiveAsync(string name, CancellationToken cancellationToken = default)
        => SendSimpleAsync($"SETACTIVE {QuoteString(name ?? string.Empty)}", "Unable to change active script", cancellationToken);

    public Task<Result> DeleteScriptAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
            return Task.FromResult(Result.Failure("Script name is required"));
        return SendSimpleAsync($"DELETESCRIPT {QuoteString(name)}", "Unable to delete script", cancellationToken);
    }

    private async Task<Result> SendSimpleAsync(string command, string failureFallback, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await SendLineAsync(command, cancellationToken);
        var response = await ReadResponseAsync(cancellationToken);
        return response.IsOk
            ? Result.Success()
            : Result.Failure(response.Message ?? failureFallback);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await SendLineAsync("LOGOUT", CancellationToken.None);
        }
        catch
        {
            // best-effort
        }
        if (_onDisposeAsync != null)
        {
            try { await _onDisposeAsync(); } catch { /* swallow */ }
        }
    }

    // ---------- Protocol helpers ----------

    private async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        var bytes = Utf8.GetBytes(line);
        await _stream.WriteAsync(bytes, cancellationToken);
        await _stream.WriteAsync(CrLf, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<Response> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var dataLines = new List<string>();
        var status = await ReadTerminatorAsync(dataLines, cancellationToken);
        return new Response(status.IsOk, status.Message, dataLines);
    }

    private async Task<Status> ReadTerminatorAsync(List<string> dataLines, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (line == null) return new Status(false, "Connection closed");

            if (IsTerminator(line, out var status)) return status;
            dataLines.Add(line);
        }
    }

    private static bool IsTerminator(string line, out Status status)
    {
        if (StartsWithKeyword(line, "OK"))
        {
            status = new Status(true, ExtractResponseMessage(line, 2));
            return true;
        }
        if (StartsWithKeyword(line, "NO"))
        {
            status = new Status(false, ExtractResponseMessage(line, 2) ?? "Operation rejected by server");
            return true;
        }
        if (StartsWithKeyword(line, "BYE"))
        {
            status = new Status(false, ExtractResponseMessage(line, 3) ?? "Server closed the connection");
            return true;
        }
        status = default;
        return false;
    }

    private static bool StartsWithKeyword(string line, string keyword)
    {
        if (!line.StartsWith(keyword, StringComparison.Ordinal)) return false;
        return line.Length == keyword.Length || line[keyword.Length] == ' ';
    }

    /// <summary>
    /// Extracts the human-readable message from an OK/NO/BYE line, skipping the optional
    /// "(code)" prefix and unquoting the trailing quoted string if present.
    /// </summary>
    private static string? ExtractResponseMessage(string line, int keywordLength)
    {
        if (line.Length <= keywordLength + 1) return null;
        var rest = line.Substring(keywordLength + 1).TrimStart();
        if (rest.Length == 0) return null;

        // Skip optional "(code)" response code.
        if (rest[0] == '(')
        {
            var close = rest.IndexOf(')');
            if (close < 0) return rest;
            rest = rest.Substring(close + 1).TrimStart();
            if (rest.Length == 0) return null;
        }

        if (rest[0] == '"')
        {
            var unquoted = TryUnquote(rest);
            if (unquoted != null) return unquoted;
        }

        return rest;
    }

    private static string? TryUnquote(string s)
    {
        if (s.Length < 2 || s[0] != '"') return null;
        var sb = new StringBuilder(s.Length - 2);
        bool escape = false;
        for (int i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (escape)
            {
                sb.Append(c);
                escape = false;
            }
            else if (c == '\\')
            {
                escape = true;
            }
            else if (c == '"')
            {
                return sb.ToString();
            }
            else
            {
                sb.Append(c);
            }
        }
        return null;
    }

    private static string QuoteString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    private async Task<SieveScriptListEntry?> ParseListEntryAsync(string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(line)) return null;

        // Quoted form: "name" [ACTIVE]
        if (line[0] == '"')
        {
            int end = -1;
            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length) { i++; continue; }
                if (line[i] == '"') { end = i; break; }
            }
            if (end < 0) return null;
            var name = TryUnquote(line.Substring(0, end + 1)) ?? string.Empty;
            var rest = line.Substring(end + 1).Trim();
            return new SieveScriptListEntry(name, IsActiveKeyword(rest));
        }

        // Literal form: {N} or {N+} on its own line, then N bytes of name, then maybe " ACTIVE\r\n"
        if (line[0] == '{' && TryParseLiteralPrefix(line, out var size))
        {
            var nameBytes = await ReadExactlyAsync(size, cancellationToken);
            var name = Utf8.GetString(nameBytes);
            var trailing = await ReadLineAsync(cancellationToken);
            return new SieveScriptListEntry(name, trailing != null && IsActiveKeyword(trailing.Trim()));
        }

        return null;
    }

    private static bool IsActiveKeyword(string s) =>
        s.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseLiteralPrefix(string line, out int size)
    {
        size = 0;
        if (line.Length < 3 || line[0] != '{') return false;
        int close = line.IndexOf('}');
        if (close < 0) return false;
        var inside = line.Substring(1, close - 1);
        if (inside.EndsWith("+", StringComparison.Ordinal)) inside = inside[..^1];
        return int.TryParse(inside, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out size);
    }

    // ---------- Low-level read helpers ----------

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            if (_readBufferPos >= _readBufferLen)
            {
                _readBufferLen = await _stream.ReadAsync(_readBuffer, cancellationToken);
                _readBufferPos = 0;
                if (_readBufferLen == 0)
                    return ms.Length == 0 ? null : Utf8.GetString(ms.ToArray());
            }
            byte b = _readBuffer[_readBufferPos++];
            if (b == 0x0A)
            {
                var bytes = ms.ToArray();
                int len = bytes.Length;
                if (len > 0 && bytes[len - 1] == 0x0D) len--;
                return Utf8.GetString(bytes, 0, len);
            }
            ms.WriteByte(b);
        }
    }

    private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellationToken)
    {
        var result = new byte[count];
        int read = 0;
        while (read < count)
        {
            if (_readBufferPos >= _readBufferLen)
            {
                _readBufferLen = await _stream.ReadAsync(_readBuffer, cancellationToken);
                _readBufferPos = 0;
                if (_readBufferLen == 0)
                    throw new IOException("Unexpected end of stream while reading literal payload");
            }
            int available = _readBufferLen - _readBufferPos;
            int toCopy = Math.Min(count - read, available);
            Buffer.BlockCopy(_readBuffer, _readBufferPos, result, read, toCopy);
            _readBufferPos += toCopy;
            read += toCopy;
        }
        return result;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ManageSieveSession));
    }

    private readonly record struct Status(bool IsOk, string? Message);

    private sealed record Response(bool IsOk, string? Message, IReadOnlyList<string> DataLines);
}
