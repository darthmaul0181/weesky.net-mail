using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Files land under a per-account directory below one root; metadata lives in process memory,
/// so a restart forgets uploads in flight — the client simply re-uploads and the sweep reclaims
/// the files left behind. TimeProvider is injected for the TTL tests; the root is overridable too.
/// </summary>
internal sealed class StagedAttachmentStore : IStagedAttachmentStore
{
    private sealed record Entry(StagedAttachmentInfo Info, string AccountId, string FilePath, DateTimeOffset StagedAt);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, long> _reserved = new();
    private readonly IOptionsMonitor<MailOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StagedAttachmentStore> _logger;
    private readonly string _root;

    public StagedAttachmentStore(
        IOptionsMonitor<MailOptions> options,
        TimeProvider clock,
        ILogger<StagedAttachmentStore> logger,
        string? root = null)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
        _root = root ?? Path.Combine(Path.GetTempPath(), "snoopy-staged");
    }

    public async Task<Result<StagedAttachmentInfo>> SaveAsync(
        string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken, string? contentId = null)
    {
        var limitMb = _options.CurrentValue.MaxMessageSizeMb;
        var limitBytes = (long)limitMb * 1024 * 1024;

        // Anti-abuse bound: one abandoned compose must not lock the account out of the next one.
        // The whole file is reserved up front, so concurrent uploads cannot all pass the same gate.
        var reservedAfter = _reserved.AddOrUpdate(accountId, limitBytes, (_, current) => current + limitBytes);
        if (reservedAfter - limitBytes >= limitBytes * 4)
        {
            Release(accountId, limitBytes);
            return Result.Failure<StagedAttachmentInfo>("Too many staged attachments; send or discard a draft first");
        }

        var id = Guid.NewGuid();
        var path = Path.Combine(_root, AccountDirectory(accountId), id.ToString("N"));

        long written = 0;
        var staged = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > limitBytes)
                        return Result.Failure<StagedAttachmentInfo>($"The attachment exceeds the {limitMb} MB limit");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            var info = new StagedAttachmentInfo(id, Path.GetFileName(fileName), written,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, contentId);
            _entries[id] = new Entry(info, accountId, path, _clock.GetUtcNow());
            staged = true;
            Release(accountId, limitBytes - written);
            return Result.Success(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not stage an attachment under {Directory}", _root);
            return Result.Failure<StagedAttachmentInfo>("The attachment could not be stored");
        }
        finally
        {
            if (!staged)
            {
                Release(accountId, limitBytes);
                TryDeleteFile(path);
            }
        }
    }

    public Result<StagedAttachment> Open(string accountId, Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.AccountId != accountId)
            return Result.Failure<StagedAttachment>("unknown_attachment");

        if (!File.Exists(entry.FilePath))
        {
            Forget(entry);
            return Result.Failure<StagedAttachment>("unknown_attachment");
        }

        return Result.Success(new StagedAttachment(entry.Info, entry.FilePath));
    }

    public void Delete(string accountId, Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.AccountId != accountId) return;

        Forget(entry);
        TryDeleteFile(entry.FilePath);
    }

    public int SweepExpired()
    {
        var deadline = _clock.GetUtcNow().AddHours(-_options.CurrentValue.StagedAttachmentTtlHours);
        var expired = _entries.Values.Where(e => e.StagedAt < deadline).ToList();

        foreach (var entry in expired)
        {
            Forget(entry);
            TryDeleteFile(entry.FilePath);
        }

        return expired.Count + SweepOrphans(deadline);
    }

    /// <summary>A restart takes the index with it; on disk, the write time is all that dates a file.</summary>
    private int SweepOrphans(DateTimeOffset deadline)
    {
        if (!Directory.Exists(_root)) return 0;

        var swept = 0;
        try
        {
            var tracked = _entries.Values.Select(e => e.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                if (tracked.Contains(path) || File.GetLastWriteTimeUtc(path) >= deadline.UtcDateTime) continue;
                if (TryDeleteFile(path)) swept++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not walk the staged attachment root {Directory}", _root);
        }

        return swept;
    }

    private static string AccountDirectory(string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)))[..16];

    private void Forget(Entry entry)
    {
        if (_entries.TryRemove(entry.Info.Id, out _)) Release(entry.AccountId, entry.Info.Size);
    }

    private void Release(string accountId, long bytes)
    {
        if (bytes > 0) _reserved.AddOrUpdate(accountId, 0, (_, current) => Math.Max(0, current - bytes));
    }

    private bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete a staged file");
            return false;
        }
    }
}
