using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Holds one IMAP session for the lifetime of the DI scope — the HTTP request.
///
/// Registered scoped, so the container disposes it at the end of the request and the connection
/// closes with it; no caller owns the session any more, which is why no repository disposes what
/// it is handed. Kept per request rather than pooled across them on purpose: the connection is
/// authenticated as one specific account with that account's own password (the Rainloop model),
/// so it is not reusable by anybody else and must not outlive the request that carried the
/// credentials.
/// </summary>
internal sealed class ScopedImapSessionProvider(
    IImapConnectionFactory factory, ILogger<ScopedImapSessionProvider> logger)
    : IImapSessionProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Host, int Port, string Username, string Password)? _key;
    private Result<IImapSession>? _session;
    private bool _disposed;

    public async Task<Result<IImapSession>> GetAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = (connection.ImapHost, connection.ImapPort, connection.Username, connection.Password);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The account never changes mid-request; a mismatch would mean the cached session
            // authenticates as somebody else, so it is replaced rather than reused.
            if (_session is { } cached && _key == key) return cached;

            await CloseAsync();

            var opened = await factory.OpenAsync(connection, cancellationToken);
            _key = key;
            _session = opened;
            return opened;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await CloseAsync();
        _gate.Dispose();
    }

    private async ValueTask CloseAsync()
    {
        if (_session is not { IsSuccess: true } open) { _session = null; return; }
        _session = null;

        try
        {
            await open.Value.DisposeAsync();
        }
        catch (Exception ex)
        {
            // The request is over either way; a failed teardown must not surface as its outcome.
            logger.LogWarning(ex, "Closing the IMAP session failed");
        }
    }
}
