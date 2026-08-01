using System.Text;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ManageSieveSessionTests
{
    private static ManageSieveSession CreateSut(string serverScript, out FakeDuplexStream stream)
    {
        stream = new FakeDuplexStream(serverScript);
        return new ManageSieveSession(stream);
    }

    // ----- LISTSCRIPTS -----

    [Fact]
    public async Task ListScriptsAsync_WithEntries_ReturnsParsedList()
    {
        var server = "\"summer\" ACTIVE\r\n\"backup\"\r\nOK\r\n";
        var sut = CreateSut(server, out var stream);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Value,
            e => { Assert.Equal("summer", e.Name); Assert.True(e.IsActive); },
            e => { Assert.Equal("backup", e.Name); Assert.False(e.IsActive); });
        Assert.Equal("LISTSCRIPTS\r\n", stream.ClientWritesAsString);
    }

    [Fact]
    public async Task ListScriptsAsync_WithNoScripts_ReturnsEmpty()
    {
        var sut = CreateSut("OK\r\n", out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ListScriptsAsync_WithLiteralName_ParsesAndDetectsActive()
    {
        // "{16+}\r\nrainloop.sieve.0 ACTIVE\r\n{4+}\r\nbak1\r\nOK\r\n"
        var server = "{16+}\r\nrainloop.sieve.0 ACTIVE\r\n{4+}\r\nbak1\r\nOK\r\n";
        var sut = CreateSut(server, out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Value,
            e => { Assert.Equal("rainloop.sieve.0", e.Name); Assert.True(e.IsActive); },
            e => { Assert.Equal("bak1", e.Name); Assert.False(e.IsActive); });
    }

    [Fact]
    public async Task ListScriptsAsync_WithMixedQuotedAndLiteralNames_ParsesBoth()
    {
        var server = "\"weesky-rules\" ACTIVE\r\n{4+}\r\nbak1\r\nOK\r\n";
        var sut = CreateSut(server, out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Value,
            e => { Assert.Equal("weesky-rules", e.Name); Assert.True(e.IsActive); },
            e => { Assert.Equal("bak1", e.Name); Assert.False(e.IsActive); });
    }

    [Fact]
    public async Task ListScriptsAsync_WithNoResponse_ReturnsFailure()
    {
        var sut = CreateSut("NO \"Something went wrong\"\r\n", out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }

    [Fact]
    public async Task ListScriptsAsync_WithByeResponse_ReturnsFailure()
    {
        var sut = CreateSut("BYE \"Goodbye\"\r\n", out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("Goodbye", result.Error);
    }

    // ----- GETSCRIPT -----

    [Fact]
    public async Task GetScriptAsync_WithLiteralPayload_ReturnsContent()
    {
        const string content = "require [\"fileinto\"];\nfileinto \"Inbox\";\n";
        var byteCount = Encoding.UTF8.GetByteCount(content);
        var server = $"{{{byteCount}}}\r\n{content}\r\nOK\r\n";
        var sut = CreateSut(server, out var stream);

        var result = await sut.GetScriptAsync("weesky-rules");

        Assert.True(result.IsSuccess);
        Assert.Equal(content, result.Value);
        Assert.Equal("GETSCRIPT \"weesky-rules\"\r\n", stream.ClientWritesAsString);
    }

    [Fact]
    public async Task GetScriptAsync_WithNonSyncLiteral_ReturnsContent()
    {
        const string content = "stop;";
        var server = $"{{5+}}\r\n{content}\r\nOK\r\n";
        var sut = CreateSut(server, out _);

        var result = await sut.GetScriptAsync("x");

        Assert.True(result.IsSuccess);
        Assert.Equal(content, result.Value);
    }

    [Fact]
    public async Task GetScriptAsync_WithNoResponse_ReturnsFailure()
    {
        var sut = CreateSut("NO (NONEXISTENT) \"Script does not exist\"\r\n", out _);

        var result = await sut.GetScriptAsync("missing");

        Assert.True(result.IsFailure);
        Assert.Equal("Script does not exist", result.Error);
    }

    // ----- PUTSCRIPT -----

    [Fact]
    public async Task PutScriptAsync_WithOk_SerialisesContentAsNonSyncLiteral()
    {
        var sut = CreateSut("OK\r\n", out var stream);
        const string content = "require [\"fileinto\"];\nfileinto \"Spam\";\n";

        var result = await sut.PutScriptAsync("weesky-rules", content);

        Assert.True(result.IsSuccess);
        var expected = $"PUTSCRIPT \"weesky-rules\" {{{Encoding.UTF8.GetByteCount(content)}+}}\r\n{content}\r\n";
        Assert.Equal(expected, stream.ClientWritesAsString);
    }

    [Fact]
    public async Task PutScriptAsync_WithCompilerError_ReturnsFailureWithMessage()
    {
        var sut = CreateSut("NO \"line 1: error: unknown command\"\r\n", out _);

        var result = await sut.PutScriptAsync("weesky-rules", "garbage;");

        Assert.True(result.IsFailure);
        Assert.Equal("line 1: error: unknown command", result.Error);
    }

    [Fact]
    public async Task PutScriptAsync_WithCompilerWarningCode_ReturnsFailureWithMessage()
    {
        var sut = CreateSut("NO (WARNINGS) \"compile warnings\"\r\n", out _);

        var result = await sut.PutScriptAsync("weesky-rules", "x");

        Assert.True(result.IsFailure);
        Assert.Equal("compile warnings", result.Error);
    }

    // ----- DELETESCRIPT -----

    [Fact]
    public async Task DeleteScriptAsync_WithOk_Succeeds()
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.DeleteScriptAsync("weesky-rules");

        Assert.True(result.IsSuccess);
        Assert.Equal("DELETESCRIPT \"weesky-rules\"\r\n", stream.ClientWritesAsString);
    }

    [Fact]
    public async Task DeleteScriptAsync_WithEmptyName_ReturnsFailure()
    {
        var sut = CreateSut(string.Empty, out _);

        var result = await sut.DeleteScriptAsync("");

        Assert.True(result.IsFailure);
    }

    // ----- SETACTIVE -----

    [Fact]
    public async Task SetActiveAsync_WithName_SendsQuotedName()
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.SetActiveAsync("weesky-rules");

        Assert.True(result.IsSuccess);
        Assert.Equal("SETACTIVE \"weesky-rules\"\r\n", stream.ClientWritesAsString);
    }

    [Fact]
    public async Task SetActiveAsync_WithEmptyName_SendsEmptyQuotedToDeactivate()
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.SetActiveAsync(string.Empty);

        Assert.True(result.IsSuccess);
        Assert.Equal("SETACTIVE \"\"\r\n", stream.ClientWritesAsString);
    }

    // ----- Dispose -----

    [Fact]
    public async Task DisposeAsync_SendsLogoutAndInvokesCallback()
    {
        var stream = new FakeDuplexStream("OK\r\n");
        var disposed = false;
        var session = new ManageSieveSession(stream, onDisposeAsync: () => { disposed = true; return ValueTask.CompletedTask; });

        await session.DisposeAsync();

        Assert.True(disposed);
        Assert.Equal("LOGOUT\r\n", stream.ClientWritesAsString);
    }

    // ----- Server-driven allocation -----

    /// <summary>
    /// The literal size is the server's word, and an external endpoint is only admin-configured:
    /// it must never be allocated on trust.
    /// </summary>
    [Fact]
    public async Task GetScriptAsync_WithAnOversizedLiteral_FailsBeforeAllocating()
    {
        var sut = CreateSut($"{{{int.MaxValue}+}}\r\n", out _);

        var result = await sut.GetScriptAsync("weesky-rules");

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.ResponseTooLarge, result.Error);
    }

    [Fact]
    public async Task ListScriptsAsync_WithAnOversizedLiteralName_FailsBeforeAllocating()
    {
        var sut = CreateSut($"{{{int.MaxValue}+}}\r\n", out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.ResponseTooLarge, result.Error);
    }

    /// <summary>A line the server never terminates is the same unbounded growth spelled differently.</summary>
    [Fact]
    public async Task ListScriptsAsync_WhenTheServerStreamsALineThatNeverEnds_StopsAtTheCeiling()
    {
        var sut = CreateSut(new string('x', (1024 * 1024) + 8), out _);

        var result = await sut.ListScriptsAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.ResponseTooLarge, result.Error);
    }

    // ----- Timeouts -----

    [Fact]
    public async Task ListScriptsAsync_WhenTheServerNeverAnswers_FailsOnTheOperationTimeout()
    {
        await using var stream = new SilentStream();
        var sut = new ManageSieveSession(stream, TimeSpan.FromMilliseconds(200));

        var result = await sut.ListScriptsAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.TimedOut, result.Error);
    }

    [Fact]
    public async Task GetScriptAsync_WhenTheServerNeverAnswers_FailsOnTheOperationTimeout()
    {
        await using var stream = new SilentStream();
        var sut = new ManageSieveSession(stream, TimeSpan.FromMilliseconds(200));

        var result = await sut.GetScriptAsync("weesky-rules").WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(result.IsFailure);
        Assert.Equal(SieveErrors.TimedOut, result.Error);
    }

    /// <summary>
    /// Disposal is the request's last act — `await using` in SieveRepository — so a LOGOUT the peer
    /// never reads must not be what holds the response back.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WhenThePeerStoppedReading_StillCompletesAndRunsTheCallback()
    {
        await using var stream = new SilentStream(blockWrites: true);
        var released = false;
        var session = new ManageSieveSession(stream, onDisposeAsync: () => { released = true; return ValueTask.CompletedTask; });

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(released);
    }

    // ----- Name escaping -----

    [Fact]
    public async Task DeleteScriptAsync_WithQuoteInName_EscapesQuote()
    {
        var sut = CreateSut("OK\r\n", out var stream);

        await sut.DeleteScriptAsync("weird\"name");

        Assert.Equal("DELETESCRIPT \"weird\\\"name\"\r\n", stream.ClientWritesAsString);
    }

    // ----- Command injection: a control character in a name must never reach the wire -----

    public static TheoryData<string> InjectingNames => new()
    {
        "weesky-rules\r\nDELETESCRIPT \"rainloop.user\"",
        "weesky-rules\nSETACTIVE \"\"",
        "weesky-rules\rX",
        "weesky-rules\0",
        "weesky\u007Frules",
        "weesky\trules",
    };

    [Theory]
    [MemberData(nameof(InjectingNames))]
    public async Task GetScriptAsync_WithControlCharacterInName_FailsWithoutWriting(string name)
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.GetScriptAsync(name);

        Assert.True(result.IsFailure);
        Assert.Contains("control character", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stream.ClientWritesAsString);
    }

    [Theory]
    [MemberData(nameof(InjectingNames))]
    public async Task PutScriptAsync_WithControlCharacterInName_FailsWithoutWriting(string name)
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.PutScriptAsync(name, "stop;");

        Assert.True(result.IsFailure);
        Assert.Contains("control character", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stream.ClientWritesAsString);
    }

    [Theory]
    [MemberData(nameof(InjectingNames))]
    public async Task SetActiveAsync_WithControlCharacterInName_FailsWithoutWriting(string name)
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.SetActiveAsync(name);

        Assert.True(result.IsFailure);
        Assert.Contains("control character", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stream.ClientWritesAsString);
    }

    [Theory]
    [MemberData(nameof(InjectingNames))]
    public async Task DeleteScriptAsync_WithControlCharacterInName_FailsWithoutWriting(string name)
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.DeleteScriptAsync(name);

        Assert.True(result.IsFailure);
        Assert.Contains("control character", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stream.ClientWritesAsString);
    }

    // The providers' real default names, plus a name a foreign webmail leaves behind.
    [Theory]
    [InlineData("weesky-rules")]
    [InlineData("rainloop.user")]
    [InlineData("rainloop.sieve.0")]
    [InlineData("Filtres perso")]
    public async Task DeleteScriptAsync_WithLegitimateName_ReachesTheWire(string name)
    {
        var sut = CreateSut("OK\r\n", out var stream);

        var result = await sut.DeleteScriptAsync(name);

        Assert.True(result.IsSuccess);
        Assert.Equal($"DELETESCRIPT \"{name}\"\r\n", stream.ClientWritesAsString);
    }

    /// <summary>A peer that accepted the socket and then stopped talking — and, optionally, stopped reading.</summary>
    private sealed class SilentStream(bool blockWrites = false) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            blockWrites ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (blockWrites) await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class FakeDuplexStream : Stream
    {
        private readonly MemoryStream _serverToClient;
        private readonly MemoryStream _clientToServer = new();

        public FakeDuplexStream(string serverScript)
        {
            _serverToClient = new MemoryStream(Encoding.UTF8.GetBytes(serverScript));
        }

        public string ClientWritesAsString => Encoding.UTF8.GetString(_clientToServer.ToArray());

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => _serverToClient.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _serverToClient.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => _clientToServer.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _clientToServer.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
