using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Holds one IMAP session for the lifetime of the DI scope — the HTTP request — and the container,
/// not a caller, disposes it. The session is per request, the socket under it need not be: with an
/// identity the client comes from <see cref="IImapConnectionPool"/>, without one it is single-use.
/// </summary>
internal sealed class ScopedImapSessionProvider(
    IImapConnectionFactory factory,
    IImapConnectionPool pool,
    IRequestIdentity identity,
    ILogger<ScopedImapSessionProvider> logger)
    : IImapSessionProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Host, int Port, string Username, MailCredential Credential)? _key;
    private Result<IImapSession>? _session;
    private bool _disposed;

    public async Task<Result<IImapSession>> GetAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = (connection.ImapHost, connection.ImapPort, connection.Username, connection.Credential);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The account never changes mid-request; a mismatch would mean the cached session
            // authenticates as somebody else, so it is replaced rather than reused.
            if (_session is { } cached && _key == key) return cached;

            await CloseAsync();

            var opened = identity.UserUid is { } uid
                ? await pool.BorrowAsync(connection, uid, cancellationToken)
                : await factory.OpenAsync(connection, cancellationToken);
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
            logger.LogWarning(ex, "Releasing the IMAP session failed");
        }
    }
}
