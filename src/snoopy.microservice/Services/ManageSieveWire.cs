using System.Globalization;
using System.Net.Security;
using System.Text;
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The ManageSieve (RFC 5804) octet layer: one stream, one read buffer, and the framing both
/// halves of a connection need — <see cref="ManageSieveClient"/> for the greeting and the SASL
/// handshake, then <see cref="ManageSieveSession"/> for the verbs.
///
/// A single instance spans the whole connection because a buffer per half would silently drop
/// whatever the first half read past its last line boundary.
/// </summary>
internal sealed class ManageSieveWire(Stream stream, IDisposable? transport = null) : IAsyncDisposable
{
    /// <summary>
    /// A Sieve script is kilobytes and a script name is bytes, so a megabyte is already generous.
    /// The figure that drives the allocation is the server's own literal prefix, and an external
    /// endpoint is admin-configured rather than trusted: without this ceiling one line of response
    /// asks for a two-gigabyte buffer.
    /// </summary>
    internal const int MaxResponseBytes = 1024 * 1024;

    internal static readonly UTF8Encoding Utf8 = new(false);

    private static readonly byte[] CrLf = [0x0D, 0x0A];

    private readonly byte[] _readBuffer = new byte[8192];
    private Stream _stream = stream;
    private int _readBufferLen;
    private int _readBufferPos;
    private string? _readFailure;
    private bool _disposed;

    /// <summary>Why the last <see cref="ReadLineAsync"/> returned null.</summary>
    internal string ReadFailure => _readFailure ?? "Connection closed";

    /// <summary>
    /// Wraps the connection in TLS, the caller supplying the policy. The SslStream becomes the
    /// wire's before it can throw, so a failed negotiation still leaves exactly one owner to
    /// dispose it and the socket beneath it.
    ///
    /// False means octets were already buffered: RFC 5804 lets the server send nothing between the
    /// STARTTLS OK and the negotiation, so they arrived while the channel was still in the clear
    /// and must not be replayed as if they had been protected.
    /// </summary>
    internal async Task<bool> TryStartTlsAsync(
        SslClientAuthenticationOptions options,
        RemoteCertificateValidationCallback validateCertificate,
        CancellationToken cancellationToken)
    {
        var clean = _readBufferPos >= _readBufferLen;
        var secured = new SslStream(_stream, leaveInnerStreamOpen: false, validateCertificate);
        _stream = secured;
        _readBufferPos = 0;
        _readBufferLen = 0;

        if (!clean) return false;

        await secured.AuthenticateAsClientAsync(options, cancellationToken);
        return true;
    }

    internal async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(Utf8.GetBytes(line), cancellationToken);
        await _stream.WriteAsync(CrLf, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    /// <summary>Writes a command whose argument is a non-synchronising literal: header, payload, CRLF.</summary>
    internal async Task WriteLiteralCommandAsync(string command, byte[] payload, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(Utf8.GetBytes($"{command} {{{payload.Length}+}}\r\n"), cancellationToken);
        await _stream.WriteAsync(payload, cancellationToken);
        await _stream.WriteAsync(CrLf, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    /// <summary>Null means the read gave up; <see cref="ReadFailure"/> says why.</summary>
    internal async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
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
            if (ms.Length >= MaxResponseBytes)
            {
                _readFailure = SieveErrors.ResponseTooLarge;
                return null;
            }
            ms.WriteByte(b);
        }
    }

    /// <summary>
    /// Reads a literal's octets, draining the line buffer first — the same buffer
    /// <see cref="ReadLineAsync"/> filled when it read the literal's header.
    /// </summary>
    internal async Task<Result<byte[]>> ReadExactlyAsync(int count, CancellationToken cancellationToken)
    {
        if (count > MaxResponseBytes)
            return Result.Failure<byte[]>(SieveErrors.ResponseTooLarge);

        var result = new byte[count];
        int read = 0;
        while (read < count)
        {
            if (_readBufferPos >= _readBufferLen)
            {
                _readBufferLen = await _stream.ReadAsync(_readBuffer, cancellationToken);
                _readBufferPos = 0;
                if (_readBufferLen == 0)
                    return Result.Failure<byte[]>(ReadFailure);
            }
            int available = _readBufferLen - _readBufferPos;
            int toCopy = Math.Min(count - read, available);
            Buffer.BlockCopy(_readBuffer, _readBufferPos, result, read, toCopy);
            _readBufferPos += toCopy;
            read += toCopy;
        }
        return result;
    }

    internal static bool TryParseStatus(string line, out Status status)
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

    internal static bool TryParseLiteralPrefix(string line, out int size)
    {
        size = 0;
        if (line.Length < 3 || line[0] != '{') return false;
        int close = line.IndexOf('}');
        if (close < 0) return false;
        var inside = line.Substring(1, close - 1);
        if (inside.EndsWith('+')) inside = inside[..^1];
        return int.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out size);
    }

    /// <summary>Unescapes a quoted string starting at <paramref name="value"/>[0], or null if unterminated.</summary>
    internal static string? Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"') return null;
        var sb = new StringBuilder(value.Length - 2);
        bool escape = false;
        for (int i = 1; i < value.Length; i++)
        {
            var c = value[i];
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

    /// <summary>Unescapes the first quoted string anywhere in the line.</summary>
    internal static string? UnquoteFirst(string line)
    {
        int start = line.IndexOf('"');
        return start < 0 ? null : Unquote(line[start..]);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _stream.DisposeAsync();
        transport?.Dispose();
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
        var rest = line[(keywordLength + 1)..].TrimStart();
        if (rest.Length == 0) return null;

        if (rest[0] == '(')
        {
            var close = rest.IndexOf(')');
            if (close < 0) return rest;
            rest = rest[(close + 1)..].TrimStart();
            if (rest.Length == 0) return null;
        }

        if (rest[0] == '"')
        {
            var unquoted = Unquote(rest);
            if (unquoted != null) return unquoted;
        }

        return rest;
    }

    internal readonly record struct Status(bool IsOk, string? Message);
}
