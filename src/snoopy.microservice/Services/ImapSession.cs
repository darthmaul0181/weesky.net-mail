using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// How a session lets go of its client on disposal — close it, or hand it back to a pool.
/// <c>healthy</c> is false when a command left the protocol in doubt; a pool must not reuse it.
/// </summary>
internal delegate ValueTask ImapClientRelease(ImapClient client, bool healthy);

/// <summary>
/// The session facade: owns the connected client, its lifetime and the shared failure contract
/// (<see cref="ExecuteAsync{T}"/>, the sentinels and the shared error constants), and delegates
/// the protocol work to <see cref="ImapFolderCommands"/> and <see cref="ImapMessageCommands"/>.
/// </summary>
internal sealed class ImapSession : IImapSession
{
    private readonly ImapClient _client;
    private readonly ImapClientRelease _release;
    private readonly ImapFolderCommands _folders;
    private readonly ImapMessageCommands _messages;
    private bool _disposed;

    public ImapSession(ImapClient client, IMailHtmlSanitizer sanitizer, ILogger logger, ImapClientRelease? release = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentNullException.ThrowIfNull(logger);
        _release = release ?? new ImapClientRelease(CloseAsync);

        DirectorySeparator = client.PersonalNamespaces.Count > 0
            ? client.PersonalNamespaces[0].DirectorySeparator
            : '/';

        _folders = new ImapFolderCommands(this, client, logger);
        _messages = new ImapMessageCommands(this, client, sanitizer, logger);
    }

    public char DirectorySeparator { get; }

    /// <summary>True once a command ended in an unrecognised exception or a cancellation: the
    /// socket may be out of sync and must be closed, never pooled.</summary>
    internal bool Tainted { get; private set; }

    public bool SupportsQuota => _client.Capabilities.HasFlag(ImapCapabilities.Quota);

    public Task<Result<Quota>> GetQuotaAsync(CancellationToken cancellationToken) =>
        _folders.GetQuotaAsync(cancellationToken);

    /// <summary>
    /// The failure contract every operation on this session shares, and the reason none of them
    /// spells it out any more: a cancellation the caller asked for propagates untouched, a known
    /// IMAP condition becomes the sentinel both layers agree on, and anything else is logged in
    /// full while the client gets one opaque sentence — the server's own words never reach it.
    ///
    /// <c>sentinel</c> maps an exception to a shared sentinel, or null when it is not one this
    /// operation recognises. Deliberately per-operation rather than global: an operation scoped
    /// to a folder passes <see cref="FolderSentinel"/>, one that also resolves a specific message
    /// by UID passes <see cref="FolderOrMessageSentinel"/>, and every method now makes that
    /// choice — none is left translating a vanished folder into the opaque failure message.
    /// </summary>
    internal async Task<Result<T>> ExecuteAsync<T>(
        CancellationToken cancellationToken,
        Func<Task<Result<T>>> operation,
        string failureMessage,
        Action<Exception> logFailure,
        Func<Exception, string?>? sentinel = null)
    {
        ThrowIfDisposed();

        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Tainted = true;
            throw;
        }
        catch (Exception ex)
        {
            if (sentinel?.Invoke(ex) is { } known) return Result.Failure<T>(known);

            // Verified on MailKit 4.17: ImapCommandException is thrown only after the tagged NO/BAD was
            // read in full (ThrowIfNotOk), so the socket is in sync and a refusal costs no reconnection.
            if (ex is not ImapCommandException) Tainted = true;
            logFailure(ex);
            return Result.Failure<T>(failureMessage);
        }
    }

    /// <inheritdoc cref="ExecuteAsync{T}"/>
    internal async Task<Result> ExecuteAsync(
        CancellationToken cancellationToken,
        Func<Task<Result>> operation,
        string failureMessage,
        Action<Exception> logFailure,
        Func<Exception, string?>? sentinel = null)
    {
        ThrowIfDisposed();

        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Tainted = true;
            throw;
        }
        catch (Exception ex)
        {
            if (sentinel?.Invoke(ex) is { } known) return Result.Failure(known);

            if (ex is not ImapCommandException) Tainted = true;
            logFailure(ex);
            return Result.Failure(failureMessage);
        }
    }

    internal static string? FolderSentinel(Exception ex) =>
        ex is FolderNotFoundException ? FolderNotFound : null;

    internal static string? MessageSentinel(Exception ex) =>
        ex is MessageNotFoundException ? MessageNotFound : null;

    internal static string? FolderOrMessageSentinel(Exception ex) =>
        FolderSentinel(ex) ?? MessageSentinel(ex);

    public Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken) =>
        _folders.ListFoldersAsync(cancellationToken);

    public Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken) =>
        _folders.CreateFolderAsync(parentPath, name, cancellationToken);

    public Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken cancellationToken) =>
        _folders.RenameFolderAsync(path, newParentPath, newName, cancellationToken);

    public Task<Result> DeleteFolderAsync(string path, CancellationToken cancellationToken) =>
        _folders.DeleteFolderAsync(path, cancellationToken);

    public Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken cancellationToken) =>
        _folders.SetSubscriptionAsync(path, subscribed, cancellationToken);

    public Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken) =>
        _folders.GetFolderStatusAsync(path, cancellationToken);

    public Task<Result> EmptyAsync(string folderPath, string? targetPath, CancellationToken cancellationToken) =>
        _folders.EmptyAsync(folderPath, targetPath, cancellationToken);

    public Task<Result> SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken cancellationToken) =>
        _messages.SetFlagsAsync(folderPath, uids, flag, value, cancellationToken);

    public Task<Result> MoveOrCopyAsync(string folderPath, IReadOnlyList<uint> uids, string targetPath, bool copy, CancellationToken cancellationToken) =>
        _messages.MoveOrCopyAsync(folderPath, uids, targetPath, copy, cancellationToken);

    public Task<Result> DeleteAsync(string folderPath, IReadOnlyList<uint> uids, CancellationToken cancellationToken) =>
        _messages.DeleteAsync(folderPath, uids, cancellationToken);

    public Task<Result> AppendAsync(string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken) =>
        _messages.AppendAsync(folderPath, message, seen, cancellationToken);

    public Task<Result<uint>> SaveDraftAsync(string folderPath, MimeMessage message, uint? replaceUid, CancellationToken cancellationToken) =>
        _messages.SaveDraftAsync(folderPath, message, replaceUid, cancellationToken);

    public Task<Result<MailSearchPage>> SearchAsync(
        string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken) =>
        _messages.SearchAsync(folderPath, allFolders, criteria, page, pageSize, cancellationToken);

    public Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, bool grouped, CancellationToken cancellationToken) =>
        _messages.ListMessagesAsync(folderPath, page, pageSize, grouped, cancellationToken);

    public Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken) =>
        _messages.GetMessageAsync(folderPath, uid, cancellationToken);

    public Task<Result<MimeMessage>> GetMimeMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken) =>
        _messages.GetMimeMessageAsync(folderPath, uid, cancellationToken);

    public Task<Result<MailMessageSource>> GetMessageSourceAsync(
        string folderPath, uint uid, int maxBytes, CancellationToken cancellationToken) =>
        _messages.GetMessageSourceAsync(folderPath, uid, maxBytes, cancellationToken);

    public Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken) =>
        _messages.GetAttachmentAsync(folderPath, uid, partSpecifier, cancellationToken);

    public const string TargetNotSelectable = "target_not_selectable";

    /// <summary>
    /// Resolves a move/empty target, failing with <see cref="TargetNotSelectable"/> when it
    /// does not exist or is a \NoSelect container — a folder that cannot hold messages.
    /// Shared by MoveOrCopyAsync and EmptyAsync so the two never drift apart on what
    /// "selectable" means.
    /// </summary>
    internal static async Task<Result<IMailFolder>> ResolveTargetOrFailAsync(
        ImapClient client, string targetPath, CancellationToken cancellationToken)
    {
        IMailFolder target;
        try { target = await client.GetFolderAsync(targetPath, cancellationToken); }
        catch (FolderNotFoundException) { return Result.Failure<IMailFolder>(TargetNotSelectable); }

        // A \NoSelect container cannot hold messages; refusing here beats a server error the
        // client cannot word. Checked by the session because the controller has no tree.
        if ((target.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) != 0)
            return Result.Failure<IMailFolder>(TargetNotSelectable);

        return Result.Success(target);
    }

    /// <summary>
    /// Sentinel errors the controller maps to 404 rather than 502. Shared constants so the
    /// two layers cannot drift apart on the exact wording.
    /// </summary>
    public const string MessageNotFound = "Message not found";
    public const string AttachmentNotFound = "Attachment not found";
    public const string FolderNotFound = "Folder not found";

    /// <summary>A leaf name refused before any IMAP round trip. Not a 404 sentinel like the three
    /// above — MailFoldersController's create/rename actions map every failure, this one
    /// included, to 502 through the ordinary FromResult path.</summary>
    public const string InvalidFolderName = "invalid_folder_name";

    /// <summary>Parent path, or null when the folder sits at the namespace root.</summary>
    public static string? ParentPath(string fullName, char separator)
    {
        var index = fullName.LastIndexOf(separator);
        return index <= 0 ? null : fullName[..index];
    }

    /// <summary>
    /// A leaf name may not contain the hierarchy separator: it would silently create a
    /// nested folder instead of the one the user asked for.
    /// </summary>
    public static bool IsValidLeafName(string name, char separator)
        => !string.IsNullOrWhiteSpace(name) && !name.Contains(separator);

    public static string CombinePath(string parentPath, string name, char separator)
        => string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}{separator}{name}";

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ImapSession));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _release(_client, healthy: !Tainted);
    }

    /// <summary>
    /// The release when nobody pools: a polite LOGOUT under its own 2 s cap when the socket is
    /// believed alive, then Dispose. Teardown runs after the response went out and must not
    /// inherit the protocol timeout. A tainted socket gets no LOGOUT — nothing is in sync to say it to.
    /// </summary>
    internal static async ValueTask CloseAsync(ImapClient client, bool healthy)
    {
        try
        {
            if (healthy && client.IsConnected)
            {
                using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.DisconnectAsync(quit: true, cap.Token);
            }
        }
        catch
        {
            // Best effort — the connection is being torn down anyway.
        }

        client.Dispose();
    }
}
