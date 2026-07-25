# Webmail 2c1 — Compose & Send Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compose and send a new HTML mail — Squire editor at `/mail/compose`, staged outgoing attachments, SMTP send with a Sent copy.

**Architecture:** Backend adds an SMTP path mirroring the IMAP one (`SmtpConnectionFactory` on the credentials-cookie model), a staged-attachment temp store with TTL GC, an outgoing HTML sanitizer distinct from the display one, and an `IMailSender` orchestration (build MIME → SMTP → APPEND Sent → purge staged). Frontend renders `ComposeView` inside the mail module (folders stay), with a thin React wrapper over Squire.

**Tech Stack:** .NET 10 / MailKit+MimeKit / Ganss+AngleSharp / xUnit+Moq — React 18 / TypeScript / TanStack Query v5 / `squire-rte` (new dep) / Vitest+RTL.

**Spec:** `docs/superpowers/specs/2026-07-23-webmail-compose-2c1-design.md`

## Global Constraints

- Folder paths never in a route segment (GUID ids are fine). Missing credentials cookie → **401** `credentials_unavailable`; mail-server refusal → **502**. `ResultEnveloppe` for errors.
- `Result<T>`/`Result` in repositories & services; controllers unwrap via `FromResult`.
- `Assert.IsType<BadRequestObjectResult>` for `BadRequest(body)`; plain `ObjectResult` only for `StatusCode(...)`.
- `dotnet test` (never `--no-build`) when new test files exist.
- UI/code/docs in **English**; comments only where the code doesn't speak (≤ 3 lines).
- Frontend: a token names a role, never a colour — **one deliberate exception here**: the editor canvas is always light (`color-scheme: light`, white), same rule as the reader iframe. Columns are band stacks (`min-height: 0` on the scrolling band).
- Frontend tests: files sit next to sources; no test lost without a replacement.
- Commits: concise, 2-line body max, never starting/ending with `@`.
- Spec refinement (decided here): `DELETE /api/Mail/Attachments/{id}` always answers **204** for the caller — the store namespace is sealed per account, so an unknown/foreign id resolves to nothing and deleting nothing is idempotent success; a 404 would leak the existence of another user's id. `POST /Send` with an unknown staged id answers **400**.
- Spec refinement (Squire): "increase/decrease indent" and "quote" collapse onto Squire's single primitive (`increaseQuoteLevel`/`decreaseQuoteLevel`) — the toolbar ships one pair of buttons, not two.

---

### Task 1: MailOptions SMTP additions + SmtpConnectionFactory

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailOptions.cs`
- Create: `src/snoopy.microservice/Services/ISmtpConnectionFactory.cs`
- Create: `src/snoopy.microservice/Services/ISmtpSession.cs`
- Create: `src/snoopy.microservice/Services/SmtpConnectionFactory.cs`
- Create: `src/snoopy.microservice/Services/SmtpSession.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Models/MailOptionsTests.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/SmtpConnectionFactoryTests.cs`

**Interfaces:**
- Produces: `ISmtpConnectionFactory.OpenAsync(string email, string password, CancellationToken) → Task<Result<ISmtpSession>>`; `ISmtpSession.SendAsync(MimeMessage, CancellationToken) → Task<Result>`; `MailOptions.MaxMessageSizeMb` (int, 25), `MailOptions.StagedAttachmentTtlHours` (int, 12), `MailOptions.IsSmtpConfigured` (bool).

- [ ] **Step 1: Failing tests**

Add to `MailOptionsTests.cs` (follow the file's existing style):

```csharp
[Fact]
public void IsSmtpConfigured_FalseWhenHostMissing()
{
    Assert.False(new MailOptions().IsSmtpConfigured);
}

[Fact]
public void IsSmtpConfigured_TrueWhenHostSet()
{
    Assert.True(new MailOptions { SmtpHost = "mail.example.org" }.IsSmtpConfigured);
}

[Fact]
public void Defaults_MatchTheSpec()
{
    var options = new MailOptions();
    Assert.Equal(25, options.MaxMessageSizeMb);
    Assert.Equal(12, options.StagedAttachmentTtlHours);
}
```

New `SmtpConnectionFactoryTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpConnectionFactoryTests
{
    private static SmtpConnectionFactory CreateFactory(MailOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new SmtpConnectionFactory(monitor.Object, NullLogger<SmtpConnectionFactory>.Instance);
    }

    [Fact]
    public async Task OpenAsync_FailsWhenSmtpIsNotConfigured()
    {
        var result = await CreateFactory(new MailOptions()).OpenAsync("a@b.c", "pw", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mail service is not configured", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ThrowsOnMissingEmail()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateFactory(new MailOptions()).OpenAsync("", "pw", CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run, verify both fail to compile / fail**

`dotnet test --filter "FullyQualifiedName~SmtpConnectionFactory|FullyQualifiedName~MailOptions"` — expect compile errors (types missing).

- [ ] **Step 3: Implement**

`MailOptions.cs` — add after `SmtpSecurity` (drop the three "Consumed when composing lands" notes, they land now):

```csharp
/// <summary>Maximum outgoing message size — sum of raw attachment bytes, in megabytes.
/// Base64 adds ~35%: keep this below Postfix's message_size_limit accordingly.</summary>
public int MaxMessageSizeMb { get; set; } = 25;

/// <summary>How long a staged attachment survives without being sent, in hours.</summary>
public int StagedAttachmentTtlHours { get; set; } = 12;
```

and beside `IsImapConfigured`:

```csharp
/// <summary>True when enough is configured to attempt an SMTP connection.</summary>
public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
```

`ISmtpSession.cs`:

```csharp
using CSharpFunctionalExtensions;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

public interface ISmtpSession : IAsyncDisposable
{
    /// <summary>Submits one message. The envelope is derived from the message's To/Cc/Bcc.</summary>
    Task<Result> SendAsync(MimeMessage message, CancellationToken cancellationToken);
}
```

`ISmtpConnectionFactory.cs`:

```csharp
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services;

public interface ISmtpConnectionFactory
{
    /// <summary>Opens an authenticated SMTP session with the user's own credentials.</summary>
    Task<Result<ISmtpSession>> OpenAsync(string email, string password, CancellationToken cancellationToken);
}
```

`SmtpConnectionFactory.cs` — mirror `ImapConnectionFactory` exactly (guard, linked timeout CTS, cert callback, `AuthenticationException` → "Mail authentication failed", ownership transfer):

```csharp
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Opens one SMTP connection per request, the same model as ImapConnectionFactory: the
/// user's own password from the credentials cookie, options through IOptionsMonitor so a
/// correction in appsettings.json applies without a restart.
/// </summary>
internal sealed class SmtpConnectionFactory : ISmtpConnectionFactory
{
    private readonly IOptionsMonitor<MailOptions> _options;
    private readonly ILogger<SmtpConnectionFactory> _logger;

    public SmtpConnectionFactory(IOptionsMonitor<MailOptions> options, ILogger<SmtpConnectionFactory> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<Result<ISmtpSession>> OpenAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));

        var options = _options.CurrentValue;

        if (!options.IsSmtpConfigured)
        {
            _logger.LogError("SMTP is not configured (Mail:SmtpHost missing)");
            return Result.Failure<ISmtpSession>("Mail service is not configured");
        }

        SmtpClient? client = null;

        try
        {
            client = new SmtpClient
            {
                ServerCertificateValidationCallback = ValidateCertificate,
                Timeout = options.TimeoutSeconds * 1000
            };

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                await client.ConnectAsync(options.SmtpHost, options.SmtpPort, options.SmtpSecurity, connectCts.Token);
                await client.AuthenticateAsync(email, password, connectCts.Token);
            }

            var session = new SmtpSession(client, _logger);
            client = null; // ownership transferred to the session
            return Result.Success<ISmtpSession>(session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            // Never echo the server's message: it can disclose account state.
            _logger.LogWarning("SMTP authentication failed for {Email}", email);
            return Result.Failure<ISmtpSession>("Mail authentication failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to connect to SMTP at {Host}:{Port}", options.SmtpHost, options.SmtpPort);
            return Result.Failure<ISmtpSession>("Unable to connect to the mail service");
        }
        finally
        {
            client?.Dispose();
        }
    }

    private bool ValidateCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;

        if (_options.CurrentValue.AllowInvalidCertificate)
        {
            _logger.LogWarning("Accepting an invalid SMTP certificate ({Errors}) — AllowInvalidCertificate is on", errors);
            return true;
        }

        _logger.LogError("Rejected the SMTP server certificate: {Errors}", errors);
        return false;
    }
}
```

`SmtpSession.cs`:

```csharp
using CSharpFunctionalExtensions;
using MailKit.Net.Smtp;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class SmtpSession : ISmtpSession
{
    private readonly SmtpClient _client;
    private readonly ILogger _logger;

    public SmtpSession(SmtpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Result> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendAsync(message, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP refused the message");
            return Result.Failure("The mail server refused the message");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _client.DisconnectAsync(quit: true); } catch { /* connection already gone */ }
        _client.Dispose();
    }
}
```

- [ ] **Step 4: Run tests, verify green** — same filter as Step 2, then `dotnet build` clean.
- [ ] **Step 5: Commit** — `Backend 2c1: SMTP connection factory on the IMAP model`

---

### Task 2: Staged attachment store + TTL sweeper

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/StagedAttachmentInfo.cs`
- Create: `src/snoopy.microservice/Services/IStagedAttachmentStore.cs`
- Create: `src/snoopy.microservice/Services/StagedAttachmentStore.cs`
- Create: `src/snoopy.microservice/Services/StagedAttachmentSweeper.cs`
- Create: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/MutableTimeProvider.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/StagedAttachmentStoreTests.cs`

**Interfaces:**
- Consumes: `MailOptions.MaxMessageSizeMb`, `MailOptions.StagedAttachmentTtlHours` (Task 1).
- Produces:

```csharp
public sealed record StagedAttachmentInfo(Guid Id, string FileName, long Size, string ContentType);
public sealed record StagedAttachment(StagedAttachmentInfo Info, string FilePath);

public interface IStagedAttachmentStore
{
    Task<Result<StagedAttachmentInfo>> SaveAsync(string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Result<StagedAttachment> Open(string accountId, Guid id);
    void Delete(string accountId, Guid id);
    int SweepExpired();
}
```

(`StagedAttachmentInfo` in `Models/Mail/StagedAttachmentInfo.cs` — one file, both records may share it since `StagedAttachment` is its close companion.)

- [ ] **Step 1: Failing tests**

`Infrastructure/MutableTimeProvider.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

internal sealed class MutableTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
```

`StagedAttachmentStoreTests.cs` — each test gets its own root directory (temp + GUID) so tests never share state; delete it in `Dispose`:

```csharp
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class StagedAttachmentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"staged-tests-{Guid.NewGuid():N}");
    private readonly MutableTimeProvider _clock = new();
    private readonly StagedAttachmentStore _store;

    public StagedAttachmentStoreTests()
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { MaxMessageSizeMb = 1, StagedAttachmentTtlHours = 12 });
        _store = new StagedAttachmentStore(monitor.Object, _clock, NullLogger<StagedAttachmentStore>.Instance, _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* nothing staged */ }
    }

    private static MemoryStream Bytes(int count) => new(new byte[count]);

    [Fact]
    public async Task SaveAsync_StoresAndOpensTheFile()
    {
        var saved = await _store.SaveAsync("me", "report.pdf", "application/pdf",
            new MemoryStream(Encoding.UTF8.GetBytes("content")), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal("report.pdf", saved.Value.FileName);
        Assert.Equal(7, saved.Value.Size);

        var opened = _store.Open("me", saved.Value.Id);
        Assert.True(opened.IsSuccess);
        Assert.Equal("content", File.ReadAllText(opened.Value.FilePath));
    }

    [Fact]
    public async Task Open_RefusesAnotherAccountsId()
    {
        var saved = await _store.SaveAsync("me", "a.txt", "text/plain", Bytes(4), CancellationToken.None);

        Assert.True(_store.Open("someone-else", saved.Value.Id).IsFailure);
    }

    [Fact]
    public async Task SaveAsync_RefusesAFileOverTheLimit()
    {
        var result = await _store.SaveAsync("me", "big.bin", "application/octet-stream",
            Bytes(1024 * 1024 + 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("1 MB", result.Error);
    }

    [Fact]
    public async Task SaveAsync_RefusesWhenTheAccountTotalWouldExceedFourTimesTheLimit()
    {
        for (var i = 0; i < 4; i++)
            Assert.True((await _store.SaveAsync("me", $"f{i}.bin", "application/octet-stream",
                Bytes(1024 * 1024), CancellationToken.None)).IsSuccess);

        var fifth = await _store.SaveAsync("me", "f5.bin", "application/octet-stream",
            Bytes(1), CancellationToken.None);

        Assert.True(fifth.IsFailure);
    }

    [Fact]
    public async Task Delete_RemovesTheFileAndIsIdempotent()
    {
        var saved = await _store.SaveAsync("me", "a.txt", "text/plain", Bytes(4), CancellationToken.None);

        _store.Delete("me", saved.Value.Id);
        _store.Delete("me", saved.Value.Id);

        Assert.True(_store.Open("me", saved.Value.Id).IsFailure);
    }

    [Fact]
    public async Task SweepExpired_RemovesOnlyWhatOutlivedTheTtl()
    {
        var old = await _store.SaveAsync("me", "old.txt", "text/plain", Bytes(4), CancellationToken.None);
        _clock.Now = _clock.Now.AddHours(13);
        var fresh = await _store.SaveAsync("me", "fresh.txt", "text/plain", Bytes(4), CancellationToken.None);

        Assert.Equal(1, _store.SweepExpired());
        Assert.True(_store.Open("me", old.Value.Id).IsFailure);
        Assert.True(_store.Open("me", fresh.Value.Id).IsSuccess);
    }
}
```

- [ ] **Step 2: Run, verify compile failure** — `dotnet test --filter FullyQualifiedName~StagedAttachmentStore`
- [ ] **Step 3: Implement**

`Models/Mail/StagedAttachmentInfo.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>What the upload endpoint answers and the compose client holds on to.</summary>
public sealed record StagedAttachmentInfo(Guid Id, string FileName, long Size, string ContentType);

/// <summary>A staged file resolved for sending. The path stays inside the store's root.</summary>
public sealed record StagedAttachment(StagedAttachmentInfo Info, string FilePath);
```

`IStagedAttachmentStore.cs`:

```csharp
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Temporary store for outgoing attachments, the Rainloop model: uploaded on add, referenced
/// by id at send time. Ids are sealed to the account that created them.
/// </summary>
public interface IStagedAttachmentStore
{
    /// <summary>Streams one upload to disk. Fails when the file or the account total exceeds the caps.</summary>
    Task<Result<StagedAttachmentInfo>> SaveAsync(string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken);

    /// <summary>Resolves one staged file. An unknown or foreign id is a plain failure.</summary>
    Result<StagedAttachment> Open(string accountId, Guid id);

    /// <summary>Removes one staged file. Removing what is already gone is a no-op.</summary>
    void Delete(string accountId, Guid id);

    /// <summary>Drops entries older than the TTL; answers how many went.</summary>
    int SweepExpired();
}
```

`StagedAttachmentStore.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Files land under a per-account directory below one root; metadata lives in process memory,
/// so a restart forgets uploads in flight — the client simply re-uploads. TimeProvider is
/// injected for the TTL tests; the root is overridable for the same reason.
/// </summary>
internal sealed class StagedAttachmentStore : IStagedAttachmentStore
{
    private sealed record Entry(StagedAttachmentInfo Info, string AccountId, string FilePath, DateTimeOffset StagedAt);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
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
        string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var limitMb = _options.CurrentValue.MaxMessageSizeMb;
        var limitBytes = (long)limitMb * 1024 * 1024;

        // Anti-abuse bound: one abandoned compose must not lock the account out of the next one.
        var accountTotal = _entries.Values.Where(e => e.AccountId == accountId).Sum(e => e.Info.Size);
        if (accountTotal >= limitBytes * 4)
            return Result.Failure<StagedAttachmentInfo>("Too many staged attachments; send or discard a draft first");

        var id = Guid.NewGuid();
        var directory = Path.Combine(_root, AccountDirectory(accountId));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id.ToString("N"));

        long written = 0;
        try
        {
            await using (var file = File.Create(path))
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
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }
        finally
        {
            if (written > limitBytes) TryDeleteFile(path);
        }

        var info = new StagedAttachmentInfo(id, Path.GetFileName(fileName), written,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        _entries[id] = new Entry(info, accountId, path, _clock.GetUtcNow());
        return Result.Success(info);
    }

    public Result<StagedAttachment> Open(string accountId, Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.AccountId != accountId || !File.Exists(entry.FilePath))
            return Result.Failure<StagedAttachment>("unknown_attachment");

        return Result.Success(new StagedAttachment(entry.Info, entry.FilePath));
    }

    public void Delete(string accountId, Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.AccountId != accountId) return;

        _entries.TryRemove(id, out _);
        TryDeleteFile(entry.FilePath);
    }

    public int SweepExpired()
    {
        var deadline = _clock.GetUtcNow().AddHours(-_options.CurrentValue.StagedAttachmentTtlHours);
        var expired = _entries.Values.Where(e => e.StagedAt < deadline).ToList();

        foreach (var entry in expired)
        {
            _entries.TryRemove(entry.Info.Id, out _);
            TryDeleteFile(entry.FilePath);
        }

        return expired.Count;
    }

    private static string AccountDirectory(string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)))[..16];

    private void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete a staged file"); }
    }
}
```

`StagedAttachmentSweeper.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Services;

/// <summary>Hourly GC over the staged store, so abandoned uploads never accumulate.</summary>
internal sealed class StagedAttachmentSweeper : BackgroundService
{
    private readonly IStagedAttachmentStore _store;
    private readonly ILogger<StagedAttachmentSweeper> _logger;

    public StagedAttachmentSweeper(IStagedAttachmentStore store, ILogger<StagedAttachmentSweeper> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = _store.SweepExpired();
            if (removed > 0) _logger.LogInformation("Swept {Count} expired staged attachments", removed);
        }
    }
}
```

- [ ] **Step 4: Run tests, verify green** — `dotnet test --filter FullyQualifiedName~StagedAttachmentStore`
- [ ] **Step 5: Commit** — `Backend 2c1: staged attachment store with TTL sweep`

---

### Task 3: Upload / delete attachment endpoints

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: `IStagedAttachmentStore` (Task 2), `FolderRoleStore.CanonicalAccountId(email)` (existing static).
- Produces: `POST /api/Mail/Attachments` (multipart `file`) → 200 `StagedAttachmentInfo` / 400 / 401(JWT only); `DELETE /api/Mail/Attachments/{id:guid}` → 204 always (sealed namespace, idempotent — see Global Constraints).
- Note: these two endpoints never touch IMAP, so they do **not** read the credentials cookie — `[Authorize]` alone gates them. The GUID id travels in the route segment: no separator hazard.
- Note: `MailController`'s constructor grows (`IStagedAttachmentStore`); update every existing construction in `MailControllerTests` mechanically.

- [ ] **Step 1: Failing tests**

Add to `MailControllerTests.cs` (reuse the file's existing mock/context helpers; add a `Mock<IStagedAttachmentStore>` to the shared setup):

```csharp
[Fact]
public async Task UploadAttachment_RefusesAMissingFile()
{
    var result = await _controller.UploadAttachment(null, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
}

[Fact]
public async Task UploadAttachment_StoresUnderTheCallersAccount()
{
    var info = new StagedAttachmentInfo(Guid.NewGuid(), "a.txt", 4, "text/plain");
    _staged.Setup(s => s.SaveAsync(
            FolderRoleStore.CanonicalAccountId("user@weesky.be"), "a.txt", "text/plain",
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(info));

    var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "a.txt")
    { Headers = new HeaderDictionary(), ContentType = "text/plain" };

    var result = await _controller.UploadAttachment(file, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Same(info, ok.Value);
}

[Fact]
public async Task UploadAttachment_AnswersBadRequestWhenTheStoreRefuses()
{
    _staged.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<StagedAttachmentInfo>("The attachment exceeds the 25 MB limit"));

    var file = new FormFile(new MemoryStream([1]), 0, 1, "file", "big.bin")
    { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };

    var result = await _controller.UploadAttachment(file, CancellationToken.None);

    Assert.IsType<ObjectResult>(result.Result); // FromResult path: StatusCode(400, enveloppe)
}

[Fact]
public void DeleteAttachment_IsIdempotentAndScoped()
{
    var id = Guid.NewGuid();

    var result = _controller.DeleteAttachment(id);

    Assert.IsType<NoContentResult>(result);
    _staged.Verify(s => s.Delete(FolderRoleStore.CanonicalAccountId("user@weesky.be"), id), Times.Once);
}
```

(Adjust the account email to whatever `MailControllerTests`' authenticated context uses.)

- [ ] **Step 2: Run, verify failure** — `dotnet test --filter FullyQualifiedName~MailControllerTests`
- [ ] **Step 3: Implement** — in `MailController`, inject the store and add:

```csharp
/// <summary>
/// Stages one outgoing attachment. Files upload as they are added — the Gmail/Rainloop
/// model — and Send references the returned ids. No IMAP involved, so no credentials
/// cookie is read. Kestrel's body cap is disabled: the store enforces the configured
/// limit itself while streaming.
/// </summary>
/// <param name="file">the uploaded file</param>
/// <param name="cancellationToken">cancellation token</param>
/// <response code="200">Id and metadata of the staged file</response>
/// <response code="400">No file, file over the limit, or account staging cap reached</response>
/// <response code="401">Not authenticated</response>
[HttpPost("Attachments")]
[DisableRequestSizeLimit]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public async Task<ActionResult<StagedAttachmentInfo>> UploadAttachment(IFormFile? file, CancellationToken cancellationToken)
{
    if (file == null || file.Length == 0)
        return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A file is required"));

    await using var content = file.OpenReadStream();
    var result = await _staged.SaveAsync(
        FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email),
        file.FileName, file.ContentType, content, cancellationToken);

    return FromResult(result);
}

/// <summary>
/// Removes one staged attachment. Always 204: the namespace is sealed per account, so an
/// unknown or foreign id resolves to nothing — and deleting nothing is idempotent success.
/// </summary>
/// <param name="id">staged attachment id</param>
/// <response code="204">Gone, or never was</response>
/// <response code="401">Not authenticated</response>
[HttpDelete("Attachments/{id:guid}")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public ActionResult DeleteAttachment(Guid id)
{
    _staged.Delete(FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email), id);
    return NoContent();
}
```

- [ ] **Step 4: Run tests, verify green** — full `dotnet test` (constructor change ripples).
- [ ] **Step 5: Commit** — `Backend 2c1: attachment staging endpoints`

---

### Task 4: Outgoing sanitizer + plain-text fallback

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/OutgoingBody.cs`
- Create: `src/snoopy.microservice/Services/IOutgoingMailSanitizer.cs`
- Create: `src/snoopy.microservice/Services/OutgoingMailSanitizer.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/OutgoingMailSanitizerTests.cs`

**Interfaces:**
- Produces: `IOutgoingMailSanitizer.Prepare(string html) → OutgoingBody`; `record OutgoingBody(string Html, string Text)`.

- [ ] **Step 1: Failing tests**

```csharp
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OutgoingMailSanitizerTests
{
    private readonly OutgoingMailSanitizer _sanitizer = new();

    [Fact]
    public void Prepare_StripsScriptsAndHandlers()
    {
        var body = _sanitizer.Prepare("<div onclick=\"x()\">hi<script>evil()</script></div>");

        Assert.DoesNotContain("script", body.Html);
        Assert.DoesNotContain("onclick", body.Html);
        Assert.Contains("hi", body.Html);
    }

    [Fact]
    public void Prepare_KeepsTheStylesTheToolbarProduces()
    {
        var body = _sanitizer.Prepare(
            "<div style=\"color: #e2674a; background-color: #ffffff; font-family: Georgia; text-align: center\">x</div>");

        Assert.Contains("color", body.Html);
        Assert.Contains("Georgia", body.Html);
        Assert.Contains("text-align", body.Html);
    }

    [Fact]
    public void Prepare_KeepsAPastedTable()
    {
        var body = _sanitizer.Prepare("<table><tr><td>cell</td></tr></table>");

        Assert.Contains("<table", body.Html);
        Assert.Contains("cell", body.Html);
    }

    [Fact]
    public void Prepare_KeepsRemoteImagesAndDropsDataUriImages()
    {
        var body = _sanitizer.Prepare(
            "<img src=\"https://example.org/a.png\"><img src=\"data:image/png;base64,AAAA\">");

        Assert.Contains("https://example.org/a.png", body.Html);
        Assert.DoesNotContain("data:", body.Html);
    }

    [Fact]
    public void Prepare_RefusesJavascriptLinks()
    {
        var body = _sanitizer.Prepare("<a href=\"javascript:evil()\">x</a>");

        Assert.DoesNotContain("javascript", body.Html);
    }

    [Fact]
    public void Prepare_DerivesAPlainTextAlternative()
    {
        var body = _sanitizer.Prepare("<div>Hello</div><div>World<br>again</div><ul><li>one</li><li>two</li></ul>");

        Assert.Equal("Hello\nWorld\nagain\none\ntwo", body.Text.Trim());
    }
}
```

- [ ] **Step 2: Run, verify compile failure** — `dotnet test --filter FullyQualifiedName~OutgoingMailSanitizer`
- [ ] **Step 3: Implement**

`Models/Mail/OutgoingBody.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>A composed body ready to send: sanitised HTML and its plain-text alternative.</summary>
public sealed record OutgoingBody(string Html, string Text);
```

`IOutgoingMailSanitizer.cs`:

```csharp
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Sanitises composed HTML for sending. A policy of its own, deliberately not
/// IMailHtmlSanitizer: that one blocks remote images and culls url() — display rules,
/// absurd on the way out.
/// </summary>
public interface IOutgoingMailSanitizer
{
    OutgoingBody Prepare(string html);
}
```

`OutgoingMailSanitizer.cs`:

```csharp
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class OutgoingMailSanitizer : IOutgoingMailSanitizer
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "li", "tr", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6", "pre", "table", "ul", "ol"
    };

    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlParser _parser = new();

    public OutgoingMailSanitizer()
    {
        // Ganss defaults are close to right for outgoing: scripts, handlers and bad schemes
        // go, styles stay. Only the scheme list is tightened.
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
    }

    public OutgoingBody Prepare(string html)
    {
        var sanitized = _sanitizer.Sanitize(html ?? string.Empty);

        var document = _parser.ParseDocument($"<body>{sanitized}</body>");
        var body = document.Body!;

        // No cid: machinery in 2c1, and most receivers block data: URIs anyway — an image
        // with no usable remote source is noise in the wire format.
        foreach (var img in body.QuerySelectorAll("img").ToList())
        {
            var src = img.GetAttribute("src") ?? string.Empty;
            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) img.Remove();
        }

        return new OutgoingBody(body.InnerHtml, ExtractText(body));
    }

    private static string ExtractText(IElement root)
    {
        var builder = new StringBuilder();
        Append(root, builder);
        var lines = builder.ToString().Split('\n').Select(l => l.Trim());
        return string.Join('\n', lines).Trim();
    }

    private static void Append(INode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is IText text) builder.Append(text.Data);
            else if (child is IElement element)
            {
                if (element.TagName.Equals("BR", StringComparison.OrdinalIgnoreCase)) { builder.Append('\n'); continue; }
                Append(element, builder);
                if (BlockTags.Contains(element.TagName)) builder.Append('\n');
            }
        }
    }
}
```

If the text test fails on exact blank lines, collapse runs of `\n` in `ExtractText` (`Regex.Replace(text, "\n{2,}", "\n")` is acceptable) — the contract is the test, one line per block.

- [ ] **Step 4: Run tests, verify green**
- [ ] **Step 5: Commit** — `Backend 2c1: outgoing sanitizer with plain-text fallback`

---

### Task 5: IMAP APPEND (session + repository)

**Files:**
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs`
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs`

**Interfaces:**
- Produces: `Task<Result> AppendAsync(string folderPath, MimeMessage message, bool seen, CancellationToken)` on both `IImapSession` and (with `User user, string password` prefix) `IMailMessageRepository`.
- Note: `ImapSession`'s MailKit-touching methods have no unit tests (existing suites cover only the pure statics — house pattern); the repository pass-through matches `MailMessageRepository`'s existing one-liner shape, and `MailSender`'s tests (Task 6) cover the call through `IMailMessageRepository`. Manual verification covers the live APPEND.

- [ ] **Step 1: Implement (no new unit test — see note; the compile is the failing gate)**

`IImapSession.cs` — add (with `using MimeKit;`):

```csharp
/// <summary>Appends a message to a folder — the Sent copy after a send.</summary>
Task<Result> AppendAsync(string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken);
```

`ImapSession.cs` — add beside the other write methods:

```csharp
public async Task<Result> AppendAsync(string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        await folder.AppendAsync(message, seen ? MessageFlags.Seen : MessageFlags.None, cancellationToken);
        return Result.Success();
    }
    catch (FolderNotFoundException)
    {
        return Result.Failure(FolderNotFound);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to append a message to {Folder}", folderPath);
        return Result.Failure("Unable to file the message");
    }
}
```

`IMailMessageRepository.cs` / `MailMessageRepository.cs` — add (repository follows its existing open-session-per-call shape):

```csharp
/// <summary>Appends a message to a folder, optionally marked read.</summary>
Task<Result> AppendAsync(User user, string password, string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken);
```

```csharp
public async Task<Result> AppendAsync(User user, string password, string folderPath, MimeMessage message, bool seen, CancellationToken cancellationToken)
{
    if (user == null) throw new ArgumentNullException(nameof(user));

    var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
    if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
    await using var session = sessionResult.Value;

    return await session.AppendAsync(folderPath, message, seen, cancellationToken);
}
```

- [ ] **Step 2: Build + full test run, verify green** — `dotnet build && dotnet test` (mocked `IImapSession` setups in existing tests are loose mocks; adding a member must not break them — fix any strict mock if one exists).
- [ ] **Step 3: Commit** — `Backend 2c1: IMAP APPEND for the Sent copy`

---

### Task 6: MailSender orchestration + Send endpoint

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/SendMessageRequest.cs`
- Create: `src/snoopy.microservice/Models/Mail/SendMessageResult.cs`
- Create: `src/snoopy.microservice/Services/IMailSender.cs`
- Create: `src/snoopy.microservice/Services/MailSender.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSenderTests.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 4, 5; `IUsersRepository.FindByEmailAsync` (FullName for the From display name — the JWT claims don't carry it); `IMailFolderRepository.GetTreeAsync` + `IFolderRoleStore.GetAsync` + `FolderRoleResolver.Resolve` (sent role, same chain the controller already uses).
- Produces: `IMailSender.SendAsync(User, string password, SendMessageRequest, CancellationToken) → Task<Result<SendMessageResult>>`; const `MailSender.UnknownAttachment = "unknown_attachment"`; `POST /api/Mail/Send` → 200 `{ appendedToSent }` / 400 / 401 / 502.

- [ ] **Step 1: Failing tests**

`Models` first (they compile the tests):

`SendMessageRequest.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// A composed message. 2c2 will add threading (inReplyTo/references) and an identity
/// choice — absent today, no dead fields in waiting.
/// </summary>
public sealed record SendMessageRequest
{
    public IReadOnlyList<string> To { get; init; } = [];
    public IReadOnlyList<string> Cc { get; init; } = [];
    public IReadOnlyList<string> Bcc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}
```

`SendMessageResult.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The mail is gone either way; false only means the Sent copy could not be filed.</summary>
public sealed record SendMessageResult(bool AppendedToSent);
```

`MailSenderTests.cs`:

```csharp
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailSenderTests
{
    private readonly Mock<IUsersRepository> _users = new();
    private readonly Mock<IOutgoingMailSanitizer> _sanitizer = new();
    private readonly Mock<IStagedAttachmentStore> _staged = new();
    private readonly Mock<ISmtpConnectionFactory> _smtpFactory = new();
    private readonly Mock<ISmtpSession> _smtp = new();
    private readonly Mock<IMailFolderRepository> _folders = new();
    private readonly Mock<IFolderRoleStore> _roles = new();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly User _user = new("mick@weesky.be");

    private MailSender CreateSender()
    {
        _users.Setup(u => u.FindByEmailAsync("mick@weesky.be"))
            .ReturnsAsync(new User("mick@weesky.be") { FullName = "Mick" });
        _sanitizer.Setup(s => s.Prepare(It.IsAny<string>()))
            .Returns(new OutgoingBody("<div>hi</div>", "hi"));
        _smtpFactory.Setup(f => f.OpenAsync("mick@weesky.be", "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(_smtp.Object));
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // A tree whose "Sent" folder carries the server flag: the resolver finds the role.
        var sent = new MailFolderNode { Name = "Sent", Path = "Sent", SpecialUse = "sent", Selectable = true };
        _folders.Setup(f => f.GetTreeAsync(_user, "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([sent]));
        _roles.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _messages.Setup(m => m.AppendAsync(_user, "pw", "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        return new MailSender(_users.Object, _sanitizer.Object, _staged.Object, _smtpFactory.Object,
            _folders.Object, _roles.Object, _messages.Object, NullLogger<MailSender>.Instance);
    }

    private static SendMessageRequest Request() => new()
    {
        To = ["alice@example.com"], Bcc = ["hidden@example.com"], Subject = "Hi", HtmlBody = "<div>hi</div>"
    };

    [Fact]
    public async Task SendAsync_BuildsFromBodyAndBccAndAppendsSeen()
    {
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await CreateSender().SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AppendedToSent);
        Assert.Equal("Mick", ((MailboxAddress)sent!.From[0]).Name);
        Assert.Equal("mick@weesky.be", ((MailboxAddress)sent.From[0]).Address);
        Assert.Equal("hidden@example.com", ((MailboxAddress)sent.Bcc[0]).Address);
        Assert.Contains("hi", sent.HtmlBody);
        Assert.Equal("hi", sent.TextBody.Trim());
        _messages.Verify(m => m.AppendAsync(_user, "pw", "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FailsOnAnUnknownAttachmentBeforeSending()
    {
        var id = Guid.NewGuid();
        _staged.Setup(s => s.Open(It.IsAny<string>(), id))
            .Returns(Result.Failure<StagedAttachment>("unknown_attachment"));

        var result = await CreateSender().SendAsync(
            _user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MailSender.UnknownAttachment, result.Error);
        _smtp.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_KeepsStagedFilesWhenSmtpRefuses()
    {
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("The mail server refused the message"));

        var result = await CreateSender().SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _staged.Verify(s => s.Delete(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
        _messages.Verify(m => m.AppendAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<MimeMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_ReportsAFailedAppendWithoutFailingTheSend()
    {
        _messages.Setup(m => m.AppendAsync(_user, "pw", "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to file the message"));

        var result = await CreateSender().SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_ReportsNoSentRoleWithoutFailingTheSend()
    {
        _folders.Setup(f => f.GetTreeAsync(_user, "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(
                [new MailFolderNode { Name = "Stuff", Path = "Stuff", Selectable = true }]));

        var result = await CreateSender().SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_PurgesStagedFilesAfterASuccessfulSend()
    {
        var id = Guid.NewGuid();
        var path = Path.GetTempFileName();
        try
        {
            _staged.Setup(s => s.Open(It.IsAny<string>(), id)).Returns(Result.Success(
                new StagedAttachment(new StagedAttachmentInfo(id, "a.txt", 4, "text/plain"), path)));

            var result = await CreateSender().SendAsync(
                _user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            _staged.Verify(s => s.Delete(It.IsAny<string>(), id), Times.Once);
        }
        finally { File.Delete(path); }
    }
}
```

(Adapt `MailFolderNode` construction to its real shape — check `Models/Mail/MailFolderNode.cs` for property names/setters; `FolderRoleResolver.Resolve` runs real, unmocked, over that tree.)

Controller tests — add to `MailControllerTests.cs` (inject `Mock<IMailSender> _sender` into the shared setup):

```csharp
[Fact]
public async Task SendMessage_RefusesWithoutARecipient()
{
    var result = await _controller.SendMessage(new SendMessageRequest(), CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
}

[Fact]
public async Task SendMessage_NamesTheInvalidAddress()
{
    var request = new SendMessageRequest { To = ["ok@example.com"], Cc = ["not-an-address"] };

    var result = await _controller.SendMessage(request, CancellationToken.None);

    var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Contains("not-an-address", ((ResultEnveloppe)bad.Value!).Message);
}

[Fact]
public async Task SendMessage_AnswersUnauthorizedWithoutCredentials()
{
    // Follow the file's existing credentials_unavailable arrangement.
    var result = await _controller.SendMessage(
        new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

    Assert.IsType<UnauthorizedObjectResult>(result.Result);
}

[Fact]
public async Task SendMessage_MapsUnknownAttachmentToBadRequest()
{
    _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
            It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<SendMessageResult>(MailSender.UnknownAttachment));

    var result = await _controller.SendMessage(
        new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(result.Result);
}

[Fact]
public async Task SendMessage_MapsAServerRefusalTo502()
{
    _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
            It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<SendMessageResult>("The mail server refused the message"));

    var result = await _controller.SendMessage(
        new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

    var status = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
}

[Fact]
public async Task SendMessage_AnswersTheSendersResult()
{
    _sender.Setup(s => s.SendAsync(It.IsAny<User>(), It.IsAny<string>(),
            It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new SendMessageResult(false)));

    var result = await _controller.SendMessage(
        new SendMessageRequest { To = ["a@example.com"] }, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.False(((SendMessageResult)ok.Value!).AppendedToSent);
}
```

- [ ] **Step 2: Run, verify failure** — `dotnet test --filter "FullyQualifiedName~MailSender|FullyQualifiedName~MailControllerTests"`
- [ ] **Step 3: Implement**

`IMailSender.cs`:

```csharp
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

public interface IMailSender
{
    /// <summary>Builds, submits over SMTP, files the Sent copy and purges the staged files.</summary>
    Task<Result<SendMessageResult>> SendAsync(User user, string password, SendMessageRequest request, CancellationToken cancellationToken);
}
```

`MailSender.cs`:

```csharp
using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The send pipeline. Order is load-bearing: staged ids resolve first (a desync fails the
/// whole request, never a partial send), SMTP failure keeps the staged files for a retry,
/// and once SMTP accepted, nothing after it may fail the operation — the mail is gone.
/// </summary>
internal sealed class MailSender : IMailSender
{
    public const string UnknownAttachment = "unknown_attachment";

    private readonly IUsersRepository _users;
    private readonly IOutgoingMailSanitizer _sanitizer;
    private readonly IStagedAttachmentStore _staged;
    private readonly ISmtpConnectionFactory _smtpFactory;
    private readonly IMailFolderRepository _folders;
    private readonly IFolderRoleStore _roles;
    private readonly IMailMessageRepository _messages;
    private readonly ILogger<MailSender> _logger;

    public MailSender(
        IUsersRepository users,
        IOutgoingMailSanitizer sanitizer,
        IStagedAttachmentStore staged,
        ISmtpConnectionFactory smtpFactory,
        IMailFolderRepository folders,
        IFolderRoleStore roles,
        IMailMessageRepository messages,
        ILogger<MailSender> logger)
    {
        _users = users;
        _sanitizer = sanitizer;
        _staged = staged;
        _smtpFactory = smtpFactory;
        _folders = folders;
        _roles = roles;
        _messages = messages;
        _logger = logger;
    }

    public async Task<Result<SendMessageResult>> SendAsync(
        User user, string password, SendMessageRequest request, CancellationToken cancellationToken)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));

        var accountId = FolderRoleStore.CanonicalAccountId(user.Email);

        var attachments = new List<StagedAttachment>();
        foreach (var id in request.AttachmentIds)
        {
            var attachment = _staged.Open(accountId, id);
            if (attachment.IsFailure) return Result.Failure<SendMessageResult>(UnknownAttachment);
            attachments.Add(attachment.Value);
        }

        var message = await BuildMessageAsync(user, request, attachments, cancellationToken);

        var smtp = await _smtpFactory.OpenAsync(user.Email, password, cancellationToken);
        if (smtp.IsFailure) return Result.Failure<SendMessageResult>(smtp.Error);
        await using (var session = smtp.Value)
        {
            var sent = await session.SendAsync(message, cancellationToken);
            if (sent.IsFailure) return Result.Failure<SendMessageResult>(sent.Error);
        }

        var appended = await AppendToSentAsync(user, password, accountId, message, cancellationToken);

        foreach (var id in request.AttachmentIds) _staged.Delete(accountId, id);

        return Result.Success(new SendMessageResult(appended));
    }

    private async Task<MimeMessage> BuildMessageAsync(
        User user, SendMessageRequest request, IReadOnlyList<StagedAttachment> attachments, CancellationToken cancellationToken)
    {
        // FullName lives in the database, not in the JWT claims.
        var dbUser = await _users.FindByEmailAsync(user.Email);
        var body = _sanitizer.Prepare(request.HtmlBody);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(dbUser?.FullName ?? string.Empty, user.Email));
        AddAddresses(message.To, request.To);
        AddAddresses(message.Cc, request.Cc);
        AddAddresses(message.Bcc, request.Bcc);
        message.Subject = request.Subject;

        var builder = new BodyBuilder { HtmlBody = body.Html, TextBody = body.Text };
        foreach (var attachment in attachments)
        {
            await using var content = File.OpenRead(attachment.FilePath);
            await builder.Attachments.AddAsync(attachment.Info.FileName, content, cancellationToken);
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static void AddAddresses(InternetAddressList list, IReadOnlyList<string> addresses)
    {
        foreach (var address in addresses) list.Add(MailboxAddress.Parse(address));
    }

    /// <summary>Best-effort by design: the mail is already gone, so every failure degrades to false.</summary>
    private async Task<bool> AppendToSentAsync(
        User user, string password, string accountId, MimeMessage message, CancellationToken cancellationToken)
    {
        var tree = await _folders.GetTreeAsync(user, password, cancellationToken);
        if (tree.IsFailure) { _logger.LogWarning("No Sent copy: folder tree unavailable"); return false; }

        var overrides = await _roles.GetAsync(accountId, cancellationToken);
        var sent = FolderRoleResolver.Resolve(tree.Value, overrides).Roles
            .FirstOrDefault(r => r.Role == "sent" && r.FolderPath != null);
        if (sent == null) { _logger.LogWarning("No Sent copy: no folder holds the sent role"); return false; }

        var appended = await _messages.AppendAsync(user, password, sent.FolderPath!, message, seen: true, cancellationToken);
        if (appended.IsFailure) _logger.LogWarning("No Sent copy: {Error}", appended.Error);
        return appended.IsSuccess;
    }
}
```

(`AttachmentCollection.AddAsync` reads the stream into the part's content buffer, so disposing right after the call is safe. Adjust the `FolderRoleStore` namespace/using to the real one, and `FolderRoleEntry`'s property names to their real shape.)

`MailController` — inject `IMailSender` and add:

```csharp
/// <summary>
/// Sends a composed message: sanitised multipart/alternative body, staged attachments,
/// Bcc in the envelope only, then a \Seen copy APPENDed to the sent role. A failed copy
/// never fails the send — the response says which happened.
/// </summary>
/// <param name="request">recipients, subject, HTML body and staged attachment ids</param>
/// <param name="cancellationToken">cancellation token</param>
/// <response code="200">Sent; appendedToSent tells whether the copy was filed</response>
/// <response code="400">No recipient, an invalid address, or a staged id no longer available</response>
/// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
/// <response code="502">The mail server refused the submission</response>
[HttpPost("Send")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
public async Task<ActionResult<SendMessageResult>> SendMessage(SendMessageRequest request, CancellationToken cancellationToken)
{
    if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
    if (request.To.Count == 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("At least one recipient is required"));

    foreach (var address in request.To.Concat(request.Cc).Concat(request.Bcc))
    {
        if (!MailboxAddress.TryParse(address, out _))
            return BadRequest(ResultEnveloppe.CreateErrorEnveloppe($"\"{address}\" is not a valid email address"));
    }

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _sender.SendAsync(AuthenticatedUser, password.Value, request, cancellationToken);

    if (result.IsFailure && result.Error == MailSender.UnknownAttachment)
        return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
            "An attachment is no longer available; remove it and attach it again"));

    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
}
```

- [ ] **Step 4: Run, verify green** — full `dotnet test`.
- [ ] **Step 5: Commit** — `Backend 2c1: MailSender pipeline and Send endpoint`

---

### Task 7: DI, configuration, backend docs

**Files:**
- Modify: `src/snoopy.microservice/Program.cs`
- Modify: `src/snoopy.microservice/appsettings.Development.json`
- Modify: `src/snoopy.microservice/CLAUDE.md`

- [ ] **Step 1: Register services** — in `Program.cs`, beside the existing mail registrations:

```csharp
builder.Services.AddSingleton<ISmtpConnectionFactory, SmtpConnectionFactory>();
builder.Services.AddSingleton<IOutgoingMailSanitizer, OutgoingMailSanitizer>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStagedAttachmentStore, StagedAttachmentStore>();
builder.Services.AddHostedService<StagedAttachmentSweeper>();
builder.Services.AddScoped<IMailSender, MailSender>();
```

(If `StagedAttachmentStore`'s optional `root` parameter confuses container resolution, register with a factory lambda passing `root: null` explicitly.)

- [ ] **Step 2: Dev config** — in `appsettings.Development.json`, add SMTP values under the existing `"Mail"` section, mirroring the IMAP host (same server, port 587, StartTls). Read the file first and follow its shape. Production config is an ops prerequisite: `Mail:SmtpHost` must be set at deploy time — same pattern as `ImapHost`.
- [ ] **Step 3: Verify** — `dotnet build && dotnet run` starts clean (Ctrl-C after the banner); full `dotnet test`.
- [ ] **Step 4: Docs** — in `src/snoopy.microservice/CLAUDE.md`, extend the `MailController` bullet with the three new endpoints (`POST /api/Mail/Attachments`, `DELETE /api/Mail/Attachments/{id}`, `POST /api/Mail/Send` with the 200-`appendedToSent` contract), and add a short "Sending" note under the Mail rules: user's own password over SMTP (same cookie), outgoing sanitizer ≠ display sanitizer, staged store sealed per account, Bcc envelope-only but kept in the Sent copy.
- [ ] **Step 5: Commit** — `Backend 2c1: wire send services, dev SMTP config, docs`

---

### Task 8: Frontend API layer + send mutation

**Files:**
- Modify: `src/frontend/src/api.js`
- Modify: `src/frontend/src/api.test.js`
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Modify: `src/frontend/src/modules/mail/queries.test.tsx`

**Interfaces:**
- Produces: `api.sendMessage(payload)`, `api.deleteAttachment(id)`, exported `uploadAttachment(file, { onProgress, signal })` (XHR — `fetch` has no upload progress), `useSendMessage()` mutation.

- [ ] **Step 1: Failing tests**

`api.test.js` additions (follow the file's existing fetch-mock idiom for the two `request()` methods):

```js
it('sendMessage posts the payload', async () => {
  // arrange fetch mock capturing the call, following the file's pattern
  await api.sendMessage({ to: ['a@b.c'], cc: [], bcc: [], subject: 's', htmlBody: '<p>x</p>', attachmentIds: [] })
  // assert URL '/api/Mail/Send', method POST, body carries to[0] === 'a@b.c'
})

it('deleteAttachment targets the id', async () => {
  await api.deleteAttachment('11111111-2222-3333-4444-555555555555')
  // assert URL '/api/Mail/Attachments/11111111-2222-3333-4444-555555555555', method DELETE
})

describe('uploadAttachment', () => {
  let sent
  class FakeXhr {
    upload = {}
    open(method, url) { this.method = method; this.url = url }
    send(form) { sent = { xhr: this, form }; }
    abort() {}
  }
  beforeEach(() => { vi.stubGlobal('XMLHttpRequest', FakeXhr) })
  afterEach(() => { vi.unstubAllGlobals() })

  it('resolves with the parsed body on 200', async () => {
    const done = uploadAttachment(new File(['x'], 'a.txt'), {})
    sent.xhr.status = 200
    sent.xhr.responseText = '{"id":"i","fileName":"a.txt","size":1,"contentType":"text/plain"}'
    sent.xhr.onload()
    await expect(done).resolves.toEqual({ id: 'i', fileName: 'a.txt', size: 1, contentType: 'text/plain' })
    expect(sent.xhr.withCredentials).toBe(true)
    expect(sent.xhr.url).toContain('/api/Mail/Attachments')
  })

  it('rejects with the enveloppe message and reports progress', async () => {
    const onProgress = vi.fn()
    const done = uploadAttachment(new File(['x'], 'a.txt'), { onProgress })
    sent.xhr.upload.onprogress({ lengthComputable: true, loaded: 1, total: 2 })
    sent.xhr.status = 400
    sent.xhr.responseText = '{"message":"The attachment exceeds the 25 MB limit"}'
    sent.xhr.onload()
    await expect(done).rejects.toThrow('The attachment exceeds the 25 MB limit')
    expect(onProgress).toHaveBeenCalledWith(0.5)
  })
})
```

`queries.test.tsx` addition — follow the file's harness (QueryClient wrapper, api mocked): `useSendMessage` calls `api.sendMessage`, and on success invalidates the folders key plus the sent folder's `messagesIn`/`messageStreamIn` when the cached tree holds a `specialUse: 'sent'` node.

- [ ] **Step 2: Run, verify failure** — `npm run test -- api.test queries.test`
- [ ] **Step 3: Implement**

`api.js` — in the Mail section:

```js
sendMessage: (payload) =>
  request('POST', '/api/Mail/Send', payload),

deleteAttachment: (id) =>
  request('DELETE', `/api/Mail/Attachments/${id}`),
```

and as a separate export beside `requestBlob`:

```js
/**
 * Uploads one outgoing attachment. XMLHttpRequest, not fetch: only XHR exposes upload
 * progress, and a 25 MB file without a bar reads as a hang.
 */
export function uploadAttachment(file, { onProgress, signal } = {}) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${BASE}/api/Mail/Attachments`)
    xhr.withCredentials = true
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress?.(event.loaded / event.total)
    }
    xhr.onload = () => {
      if (xhr.status === 401) {
        clearSession()
        unauthorizedHandler?.()
        reject(new ApiError('Unauthorized', 401, null))
        return
      }
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText))
        return
      }
      let message = xhr.responseText
      try {
        const parsed = JSON.parse(xhr.responseText)
        message = parsed?.message ?? parsed?.Message ?? message
      } catch { /* raw text stays */ }
      reject(new ApiError(message || xhr.statusText, xhr.status, typeof message === 'string' ? message : null))
    }
    xhr.onerror = () => reject(new ApiError('Network error', 0, null))
    signal?.addEventListener('abort', () => { xhr.abort(); reject(new ApiError('Aborted', 0, null)) })
    const form = new FormData()
    form.append('file', file)
    xhr.send(form)
  })
}
```

`queries.ts`:

```ts
export interface SendMessageArgs {
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  htmlBody: string
  attachmentIds: string[]
}

export interface SendMessageResult { appendedToSent: boolean }

export function useSendMessage() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: (args: SendMessageArgs) => api.sendMessage(args) as Promise<SendMessageResult>,
    onSuccess: () => {
      // The copy changes the sent folder's counts and list; the poll would catch up in a
      // minute, an invalidate shows it now.
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
      const folders = queryClient.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      const sent = folders ? flatten(folders).find(e => e.node.specialUse === 'sent') : undefined
      if (sent) {
        queryClient.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, sent.node.path) })
        queryClient.invalidateQueries({ queryKey: mailKeys.messageStreamIn(accountId, sent.node.path) })
      }
    },
  })
}
```

- [ ] **Step 4: Run, verify green** — `npm run test -- api.test queries.test` then `npm run lint`.
- [ ] **Step 5: Commit** — `Frontend 2c1: send/upload API layer and mutation`

---

### Task 9: Squire wrapper + editor toolbar

**Files:**
- Modify: `src/frontend/package.json` (add `squire-rte`)
- Create: `src/frontend/src/modules/mail/compose/SquireEditor.tsx`
- Create: `src/frontend/src/modules/mail/compose/EditorToolbar.tsx`
- Test: `src/frontend/src/modules/mail/compose/SquireEditor.test.tsx`
- Test: `src/frontend/src/modules/mail/compose/EditorToolbar.test.tsx`
- Modify: `src/frontend/src/styles/mail.css` (compose editor styles — see Task 12 for the full block; this task adds only `.compose-editor` and `.compose-toolbar`)

**Interfaces:**
- Produces:

```ts
export type EditorCommand =
  | 'undo' | 'redo'
  | 'bold' | 'italic' | 'underline' | 'strikethrough'
  | 'unorderedList' | 'orderedList'
  | 'increaseQuote' | 'decreaseQuote'
  | 'removeLink' | 'clearFormatting'

export interface EditorHandle {
  getHTML: () => string
  isEmpty: () => boolean
  focus: () => void
  command: (name: EditorCommand) => void
  setTextColour: (colour: string) => void
  setHighlightColour: (colour: string) => void
  setFontFace: (face: string) => void
  setFontSize: (size: string) => void
  setAlignment: (alignment: 'left' | 'center' | 'right' | 'justify') => void
  makeLink: (url: string) => void
}
```

`<SquireEditor ref onChange={() => …} />` — `onChange` fires on every Squire `input` event (the dirty signal). `<EditorToolbar editor={EditorHandle | null} />`.

- [ ] **Step 1: Install** — `npm install squire-rte` (in `src/frontend/`). If the package ships no TypeScript types, add `src/frontend/src/types/squire-rte.d.ts`:

```ts
declare module 'squire-rte' {
  export default class Squire {
    constructor(root: HTMLElement, config?: object)
    // Only what the wrapper touches; the real surface is larger.
    getHTML(): string
    setHTML(html: string): void
    addEventListener(type: string, handler: () => void): void
    destroy(): void
    focus(): void
    undo(): void
    redo(): void
    bold(): void; removeBold(): void
    italic(): void; removeItalic(): void
    underline(): void; removeUnderline(): void
    strikethrough(): void; removeStrikethrough(): void
    hasFormat(tag: string): boolean
    makeUnorderedList(): void; makeOrderedList(): void; removeList(): void
    increaseQuoteLevel(): void; decreaseQuoteLevel(): void
    makeLink(url: string): void; removeLink(): void
    setTextColour(colour: string): void
    setHighlightColour(colour: string): void
    setFontFace(face: string): void
    setFontSize(size: string): void
    setTextAlignment(alignment: string): void
    removeAllFormatting(): void
  }
}
```

- [ ] **Step 2: Failing tests**

`SquireEditor.test.tsx` — jsdom's Range/Selection support is too partial for the real engine, so Squire is mocked; these tests cover **our glue** (mount/destroy, command relay, toggle logic, onChange), never Squire itself:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createRef } from 'react'
import { render } from '@testing-library/react'
import SquireEditor, { type EditorHandle } from './SquireEditor'

const instance = {
  getHTML: vi.fn(() => '<div>hi</div>'),
  setHTML: vi.fn(),
  addEventListener: vi.fn(),
  destroy: vi.fn(),
  focus: vi.fn(),
  undo: vi.fn(), redo: vi.fn(),
  bold: vi.fn(), removeBold: vi.fn(),
  italic: vi.fn(), removeItalic: vi.fn(),
  underline: vi.fn(), removeUnderline: vi.fn(),
  strikethrough: vi.fn(), removeStrikethrough: vi.fn(),
  hasFormat: vi.fn(() => false),
  makeUnorderedList: vi.fn(), makeOrderedList: vi.fn(), removeList: vi.fn(),
  increaseQuoteLevel: vi.fn(), decreaseQuoteLevel: vi.fn(),
  makeLink: vi.fn(), removeLink: vi.fn(),
  setTextColour: vi.fn(), setHighlightColour: vi.fn(),
  setFontFace: vi.fn(), setFontSize: vi.fn(), setTextAlignment: vi.fn(),
  removeAllFormatting: vi.fn(),
}
vi.mock('squire-rte', () => ({ default: vi.fn(() => instance) }))

function setup() {
  const ref = createRef<EditorHandle>()
  const onChange = vi.fn()
  const view = render(<SquireEditor ref={ref} onChange={onChange} />)
  return { ref, onChange, view }
}

describe('SquireEditor', () => {
  beforeEach(() => vi.clearAllMocks())

  it('relays commands and reads HTML through the handle', () => {
    const { ref } = setup()
    ref.current!.command('bold')
    expect(instance.bold).toHaveBeenCalled()
    expect(ref.current!.getHTML()).toBe('<div>hi</div>')
  })

  it('toggles a format off when it is already applied', () => {
    const { ref } = setup()
    instance.hasFormat.mockReturnValue(true)
    ref.current!.command('bold')
    expect(instance.removeBold).toHaveBeenCalled()
    expect(instance.bold).not.toHaveBeenCalled()
  })

  it('fires onChange on the input event', () => {
    const { onChange } = setup()
    const inputHandler = instance.addEventListener.mock.calls.find(c => c[0] === 'input')![1]
    inputHandler()
    expect(onChange).toHaveBeenCalled()
  })

  it('destroys the engine on unmount', () => {
    const { view } = setup()
    view.unmount()
    expect(instance.destroy).toHaveBeenCalled()
  })
})
```

`EditorToolbar.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import EditorToolbar from './EditorToolbar'
import type { EditorHandle } from './SquireEditor'

function fakeEditor(): EditorHandle {
  return {
    getHTML: vi.fn(() => ''), isEmpty: vi.fn(() => true), focus: vi.fn(),
    command: vi.fn(), setTextColour: vi.fn(), setHighlightColour: vi.fn(),
    setFontFace: vi.fn(), setFontSize: vi.fn(), setAlignment: vi.fn(), makeLink: vi.fn(),
  }
}

describe('EditorToolbar', () => {
  it('relays a format button to the editor', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))
    expect(editor.command).toHaveBeenCalledWith('bold')
  })

  it('applies a text colour from the swatch grid', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    fireEvent.click(screen.getByRole('button', { name: '#d0021b' }))
    expect(editor.setTextColour).toHaveBeenCalledWith('#d0021b')
  })

  it('applies font, size and alignment from the selects', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.change(screen.getByLabelText('Font'), { target: { value: 'Georgia' } })
    fireEvent.change(screen.getByLabelText('Size'), { target: { value: '18px' } })
    fireEvent.change(screen.getByLabelText('Alignment'), { target: { value: 'center' } })
    expect(editor.setFontFace).toHaveBeenCalledWith('Georgia')
    expect(editor.setFontSize).toHaveBeenCalledWith('18px')
    expect(editor.setAlignment).toHaveBeenCalledWith('center')
  })

  it('inserts a link through the URL popover', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(editor.makeLink).toHaveBeenCalledWith('https://weesky.net')
  })

  it('does nothing without an editor', () => {
    render(<EditorToolbar editor={null} />)
    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))
    // no throw is the assertion
  })
})
```

- [ ] **Step 3: Run, verify failure** — `npm run test -- compose`
- [ ] **Step 4: Implement**

`SquireEditor.tsx`:

```tsx
import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react'
import Squire from 'squire-rte'

export type EditorCommand =
  | 'undo' | 'redo'
  | 'bold' | 'italic' | 'underline' | 'strikethrough'
  | 'unorderedList' | 'orderedList'
  | 'increaseQuote' | 'decreaseQuote'
  | 'removeLink' | 'clearFormatting'

export interface EditorHandle {
  getHTML: () => string
  isEmpty: () => boolean
  focus: () => void
  command: (name: EditorCommand) => void
  setTextColour: (colour: string) => void
  setHighlightColour: (colour: string) => void
  setFontFace: (face: string) => void
  setFontSize: (size: string) => void
  setAlignment: (alignment: 'left' | 'center' | 'right' | 'justify') => void
  makeLink: (url: string) => void
}

interface Props { onChange: () => void }

// Format toggles pair the apply call with its remove and the tag hasFormat checks.
const toggles: Record<string, [keyof Squire, keyof Squire, string]> = {
  bold: ['bold', 'removeBold', 'b'],
  italic: ['italic', 'removeItalic', 'i'],
  underline: ['underline', 'removeUnderline', 'u'],
  strikethrough: ['strikethrough', 'removeStrikethrough', 's'],
  unorderedList: ['makeUnorderedList', 'removeList', 'ul'],
  orderedList: ['makeOrderedList', 'removeList', 'ol'],
}

/**
 * Thin React shell over Squire. The canvas is always light — same rule as the reader
 * iframe: the compose shows what the recipient will see, whatever the app theme.
 */
const SquireEditor = forwardRef<EditorHandle, Props>(function SquireEditor({ onChange }, ref) {
  const root = useRef<HTMLDivElement>(null)
  const editor = useRef<Squire | null>(null)

  useEffect(() => {
    const squire = new Squire(root.current!, { blockTag: 'DIV' })
    squire.addEventListener('input', onChange)
    editor.current = squire
    return () => { squire.destroy(); editor.current = null }
    // Mount once: onChange identity is the caller's concern, rebinding would rebuild the editor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useImperativeHandle(ref, () => ({
    getHTML: () => editor.current?.getHTML() ?? '',
    isEmpty: () => {
      const html = editor.current?.getHTML() ?? ''
      return html.replace(/<[^>]*>/g, '').trim() === ''
    },
    focus: () => editor.current?.focus(),
    command: (name) => {
      const squire = editor.current
      if (!squire) return
      const toggle = toggles[name]
      if (toggle) {
        const [apply, remove, tag] = toggle
        void (squire.hasFormat(tag) ? (squire[remove] as () => void)() : (squire[apply] as () => void)())
        return
      }
      if (name === 'undo') squire.undo()
      else if (name === 'redo') squire.redo()
      else if (name === 'increaseQuote') squire.increaseQuoteLevel()
      else if (name === 'decreaseQuote') squire.decreaseQuoteLevel()
      else if (name === 'removeLink') squire.removeLink()
      else if (name === 'clearFormatting') squire.removeAllFormatting()
    },
    setTextColour: (colour) => editor.current?.setTextColour(colour),
    setHighlightColour: (colour) => editor.current?.setHighlightColour(colour),
    setFontFace: (face) => editor.current?.setFontFace(face),
    setFontSize: (size) => editor.current?.setFontSize(size),
    setAlignment: (alignment) => editor.current?.setTextAlignment(alignment),
    makeLink: (url) => editor.current?.makeLink(url),
  }), [])

  return <div ref={root} className="compose-editor" data-testid="compose-editor" />
})

export default SquireEditor
```

`EditorToolbar.tsx` (single file; the swatch and link popovers are local components — close on apply and on outside `mousedown`, the `AvatarMenu` pattern):

```tsx
import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { EditorHandle } from './SquireEditor'

const SWATCHES = [
  '#000000', '#444444', '#666666', '#999999', '#cccccc', '#ffffff',
  '#d0021b', '#e2674a', '#f5a623', '#f8e71c', '#7ed321', '#417505',
  '#4a90d9', '#182238', '#9013fe', '#bd10e0', '#8b572a', '#50e3c2',
]
const FONTS = ['Arial', 'Georgia', 'Tahoma', 'Times New Roman', 'Verdana', 'Courier New']
const SIZES = [
  { label: 'Small', value: '12px' }, { label: 'Normal', value: '14px' },
  { label: 'Large', value: '18px' }, { label: 'Huge', value: '24px' },
]

function Popover({ open, children }: { open: boolean; children: ReactNode }) {
  if (!open) return null
  return <div className="compose-popover">{children}</div>
}

interface Props { editor: EditorHandle | null }

export default function EditorToolbar({ editor }: Props) {
  const [openPopover, setOpenPopover] = useState<'text' | 'highlight' | 'link' | null>(null)
  const [url, setUrl] = useState('')
  const container = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function onDown(event: MouseEvent) {
      if (!container.current?.contains(event.target as Node)) setOpenPopover(null)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [])

  function swatchGrid(apply: (colour: string) => void) {
    return (
      <div className="compose-swatches">
        {SWATCHES.map(colour => (
          <button key={colour} type="button" aria-label={colour} style={{ background: colour }}
            onClick={() => { apply(colour); setOpenPopover(null) }} />
        ))}
      </div>
    )
  }

  const btn = (label: string, glyph: ReactNode, onClick: () => void) => (
    <button type="button" className="compose-tool" aria-label={label} title={label} onClick={onClick}>
      {glyph}
    </button>
  )

  return (
    <div className="compose-toolbar" ref={container}>
      {btn('Undo', '↶', () => editor?.command('undo'))}
      {btn('Redo', '↷', () => editor?.command('redo'))}
      <span className="compose-toolbar-rule" />
      {btn('Bold', <b>B</b>, () => editor?.command('bold'))}
      {btn('Italic', <i>I</i>, () => editor?.command('italic'))}
      {btn('Underline', <u>U</u>, () => editor?.command('underline'))}
      {btn('Strikethrough', <s>S</s>, () => editor?.command('strikethrough'))}
      <span className="compose-toolbar-rule" />
      <span className="compose-popover-anchor">
        {btn('Text colour', 'A', () => setOpenPopover(p => p === 'text' ? null : 'text'))}
        <Popover open={openPopover === 'text'}>{swatchGrid(c => editor?.setTextColour(c))}</Popover>
      </span>
      <span className="compose-popover-anchor">
        {btn('Highlight colour', '▩', () => setOpenPopover(p => p === 'highlight' ? null : 'highlight'))}
        <Popover open={openPopover === 'highlight'}>{swatchGrid(c => editor?.setHighlightColour(c))}</Popover>
      </span>
      <label className="compose-select">
        <span className="visually-hidden">Font</span>
        <select aria-label="Font" defaultValue="Arial" onChange={e => editor?.setFontFace(e.target.value)}>
          {FONTS.map(font => <option key={font} value={font}>{font}</option>)}
        </select>
      </label>
      <label className="compose-select">
        <span className="visually-hidden">Size</span>
        <select aria-label="Size" defaultValue="14px" onChange={e => editor?.setFontSize(e.target.value)}>
          {SIZES.map(size => <option key={size.value} value={size.value}>{size.label}</option>)}
        </select>
      </label>
      <label className="compose-select">
        <span className="visually-hidden">Alignment</span>
        <select aria-label="Alignment" defaultValue="left"
          onChange={e => editor?.setAlignment(e.target.value as 'left' | 'center' | 'right' | 'justify')}>
          <option value="left">Left</option><option value="center">Center</option>
          <option value="right">Right</option><option value="justify">Justify</option>
        </select>
      </label>
      <span className="compose-toolbar-rule" />
      {btn('Bulleted list', '•', () => editor?.command('unorderedList'))}
      {btn('Numbered list', '1.', () => editor?.command('orderedList'))}
      {btn('Increase quote', '❯❯', () => editor?.command('increaseQuote'))}
      {btn('Decrease quote', '❮❮', () => editor?.command('decreaseQuote'))}
      <span className="compose-toolbar-rule" />
      <span className="compose-popover-anchor">
        {btn('Link', '🔗', () => setOpenPopover(p => p === 'link' ? null : 'link'))}
        <Popover open={openPopover === 'link'}>
          <div className="compose-link-form">
            <label htmlFor="compose-link-url">Link URL</label>
            <input id="compose-link-url" type="url" value={url} onChange={e => setUrl(e.target.value)} />
            <button type="button" className="btn btn-primary" disabled={!url}
              onClick={() => { editor?.makeLink(url); setUrl(''); setOpenPopover(null) }}>
              Apply
            </button>
          </div>
        </Popover>
      </span>
      {btn('Remove link', '⛓', () => editor?.command('removeLink'))}
      {btn('Clear formatting', '⌫', () => editor?.command('clearFormatting'))}
    </div>
  )
}
```

(Real glyphs: reuse existing icon components where one fits — `PencilIcon` etc. are available; text glyphs are acceptable where no icon exists. `visually-hidden` may already exist in the CSS; add it if not.)

Append to `mail.css`:

```css
/* ── Compose: editor ─────────────────────────────────────────────────────── */
/* The canvas is deliberately light in every theme — like the reader iframe, it shows
   what the recipient will see. The one sanctioned hard-coded colour pair. */
.compose-editor {
  flex: 1; min-height: 0; overflow-y: auto;
  background: #ffffff; color: #111111; color-scheme: light;
  padding: 12px 16px; outline: none;
}
.compose-toolbar {
  display: flex; align-items: center; gap: 2px; flex-wrap: wrap;
  padding: 4px 8px; border-bottom: 1px solid var(--border);
}
.compose-tool {
  background: none; border: none; cursor: pointer; color: var(--text);
  width: 28px; height: 28px; border-radius: var(--radius-sm);
  display: inline-flex; align-items: center; justify-content: center;
}
.compose-tool:hover { background: var(--surface-sunken); }
.compose-toolbar-rule { width: 1px; height: 20px; background: var(--border); margin: 0 4px; }
.compose-select select { border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--surface); color: var(--text); padding: 2px 4px; font-size: 12px; }
.compose-popover-anchor { position: relative; }
.compose-popover {
  position: absolute; top: 100%; left: 0; z-index: 20;
  background: var(--surface-raised); border: 1px solid var(--border);
  border-radius: var(--radius-md); padding: 8px; box-shadow: 0 4px 16px rgb(0 0 0 / 0.2);
}
.compose-swatches { display: grid; grid-template-columns: repeat(6, 20px); gap: 4px; }
.compose-swatches button { width: 20px; height: 20px; border: 1px solid var(--border); border-radius: 3px; cursor: pointer; }
.compose-link-form { display: flex; align-items: center; gap: 8px; }
.compose-link-form input { width: 220px; }
```

- [ ] **Step 5: Run, verify green** — `npm run test -- compose`, `npm run lint`, `npm run typecheck`.
- [ ] **Step 6: Commit** — `Frontend 2c1: Squire wrapper and editor toolbar`

---

### Task 10: RecipientsField (token input)

**Files:**
- Create: `src/frontend/src/modules/mail/compose/RecipientsField.tsx`
- Test: `src/frontend/src/modules/mail/compose/RecipientsField.test.tsx`
- Modify: `src/frontend/src/styles/mail.css`

**Interfaces:**
- Produces: `<RecipientsField id label tokens onChange />` with `tokens: string[]`; exported pure `isValidAddress(value: string): boolean` (`/^[^\s@]+@[^\s@]+\.[^\s@]+$/` — the backend's MimeKit parse is the authority; this only paints and gates locally).

- [ ] **Step 1: Failing tests**

```tsx
import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import RecipientsField, { isValidAddress } from './RecipientsField'

function setup(tokens: string[] = []) {
  const onChange = vi.fn()
  render(<RecipientsField id="to" label="To" tokens={tokens} onChange={onChange} />)
  return { onChange }
}

describe('isValidAddress', () => {
  it.each(['a@b.co', 'first.last@sub.domain.org'])('accepts %s', v => expect(isValidAddress(v)).toBe(true))
  it.each(['nope', 'a@b', 'a b@c.d', '@x.y'])('refuses %s', v => expect(isValidAddress(v)).toBe(false))
})

describe('RecipientsField', () => {
  it('commits a token on Enter', () => {
    const { onChange } = setup()
    fireEvent.change(screen.getByLabelText('To'), { target: { value: 'a@b.co' } })
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Enter' })
    expect(onChange).toHaveBeenCalledWith(['a@b.co'])
  })

  it('commits on comma, semicolon and blur', () => {
    const { onChange } = setup()
    const input = screen.getByLabelText('To')
    fireEvent.change(input, { target: { value: 'a@b.co' } })
    fireEvent.keyDown(input, { key: ',' })
    expect(onChange).toHaveBeenCalledWith(['a@b.co'])
    fireEvent.change(input, { target: { value: 'c@d.co' } })
    fireEvent.blur(input)
    expect(onChange).toHaveBeenLastCalledWith(['c@d.co'])
  })

  it('splits a paste on separators', () => {
    const { onChange } = setup()
    const input = screen.getByLabelText('To')
    fireEvent.paste(input, { clipboardData: { getData: () => 'a@b.co, c@d.co; e@f.co' } })
    expect(onChange).toHaveBeenCalledWith(['a@b.co', 'c@d.co', 'e@f.co'])
  })

  it('marks an invalid token and removes on its ✕', () => {
    const { onChange } = setup(['bad-token', 'ok@x.co'])
    expect(screen.getByText('bad-token').closest('.recipient-token')).toHaveClass('is-invalid')
    fireEvent.click(screen.getAllByRole('button', { name: /^Remove / })[0])
    expect(onChange).toHaveBeenCalledWith(['ok@x.co'])
  })

  it('Backspace on an empty input removes the last token', () => {
    const { onChange } = setup(['a@b.co'])
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Backspace' })
    expect(onChange).toHaveBeenCalledWith([])
  })
})
```

- [ ] **Step 2: Run, verify failure** — `npm run test -- RecipientsField`
- [ ] **Step 3: Implement**

```tsx
import { useState, type ClipboardEvent, type KeyboardEvent } from 'react'

/** Paint-and-gate check only; the backend's MimeKit parse is the authority. */
export function isValidAddress(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

interface Props {
  id: string
  label: string
  tokens: string[]
  onChange: (tokens: string[]) => void
  autoFocus?: boolean
}

export default function RecipientsField({ id, label, tokens, onChange, autoFocus }: Props) {
  const [draft, setDraft] = useState('')

  function commit(raw: string) {
    const parts = raw.split(/[,;]/).map(p => p.trim()).filter(Boolean)
    if (parts.length > 0) onChange([...tokens, ...parts])
    setDraft('')
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' || event.key === ',' || event.key === ';') {
      event.preventDefault()
      if (draft.trim()) commit(draft)
    } else if (event.key === 'Backspace' && draft === '' && tokens.length > 0) {
      onChange(tokens.slice(0, -1))
    }
  }

  function onPaste(event: ClipboardEvent<HTMLInputElement>) {
    const text = event.clipboardData.getData('text')
    if (!/[,;]/.test(text)) return
    event.preventDefault()
    commit(text)
  }

  return (
    <div className="field-h recipients-field">
      <label htmlFor={id}>{label}</label>
      <div className="recipients-box">
        {tokens.map((token, index) => (
          <span key={`${token}-${index}`} className={`recipient-token${isValidAddress(token) ? '' : ' is-invalid'}`}>
            {token}
            <button type="button" aria-label={`Remove ${token}`}
              onClick={() => onChange(tokens.filter((_, i) => i !== index))}>✕</button>
          </span>
        ))}
        <input id={id} type="text" value={draft} autoFocus={autoFocus}
          onChange={e => setDraft(e.target.value)}
          onKeyDown={onKeyDown} onPaste={onPaste}
          onBlur={() => { if (draft.trim()) commit(draft) }} />
      </div>
    </div>
  )
}
```

CSS:

```css
/* ── Compose: recipients ─────────────────────────────────────────────────── */
.recipients-box {
  flex: 1; display: flex; flex-wrap: wrap; gap: 4px; align-items: center;
  border: 1px solid var(--border); border-radius: var(--radius-sm);
  background: var(--surface); padding: 4px 6px; min-height: 32px;
}
.recipients-box input { flex: 1; min-width: 140px; border: none; background: none; color: var(--text); outline: none; }
.recipient-token {
  display: inline-flex; align-items: center; gap: 4px;
  background: var(--surface-sunken); border-radius: 999px; padding: 2px 8px; font-size: 12px;
}
.recipient-token.is-invalid { color: var(--danger); border: 1px solid var(--danger); }
.recipient-token button { background: none; border: none; cursor: pointer; color: inherit; padding: 0; }
```

- [ ] **Step 4: Run, verify green** — `npm run test -- RecipientsField`, `npm run lint`.
- [ ] **Step 5: Commit** — `Frontend 2c1: recipients token field`

---

### Task 11: Attachment tray + staged-upload hook

**Files:**
- Create: `src/frontend/src/modules/mail/compose/useStagedAttachments.ts`
- Create: `src/frontend/src/modules/mail/compose/AttachmentTray.tsx`
- Test: `src/frontend/src/modules/mail/compose/useStagedAttachments.test.tsx`
- Test: `src/frontend/src/modules/mail/compose/AttachmentTray.test.tsx`
- Modify: `src/frontend/src/styles/mail.css`

**Interfaces:**
- Consumes: `uploadAttachment` / `api.deleteAttachment` (Task 8), `formatSize` (existing, `reader/formatSize.ts`).
- Produces:

```ts
export interface StagedItem {
  key: string            // local identity, survives the id-less uploading phase
  id: string | null      // server id once staged
  fileName: string
  size: number
  progress: number       // 0..1
  error: string | null
}
export interface StagedAttachmentsApi {
  items: StagedItem[]
  addFiles: (files: FileList | File[]) => void
  remove: (key: string) => void          // DELETEs server-side when staged
  discardAll: () => void                 // best-effort DELETE of every staged id
  uploading: boolean                     // any item still in flight
  ids: string[]                          // staged ids, send payload
}
```

- [ ] **Step 1: Failing tests**

`useStagedAttachments.test.tsx` — mock `../../../api.js` (`uploadAttachment`, `api.deleteAttachment`); drive `uploadAttachment` with a controllable deferred:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useStagedAttachments } from './useStagedAttachments'
import { uploadAttachment, api } from '../../../api.js'

vi.mock('../../../api.js', () => ({
  uploadAttachment: vi.fn(),
  api: { deleteAttachment: vi.fn().mockResolvedValue(null) },
}))

const file = new File(['abcd'], 'a.txt', { type: 'text/plain' })

describe('useStagedAttachments', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uploads on add and stores the returned id', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments())

    await act(async () => { result.current.addFiles([file]) })

    expect(result.current.items[0]).toMatchObject({ id: 'id-1', fileName: 'a.txt', progress: 1, error: null })
    expect(result.current.uploading).toBe(false)
    expect(result.current.ids).toEqual(['id-1'])
  })

  it('reports uploading while a file is in flight', async () => {
    let resolve!: (v: unknown) => void
    vi.mocked(uploadAttachment).mockReturnValue(new Promise(r => { resolve = r }))
    const { result } = renderHook(() => useStagedAttachments())

    act(() => { result.current.addFiles([file]) })
    expect(result.current.uploading).toBe(true)

    await act(async () => { resolve({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' }) })
    expect(result.current.uploading).toBe(false)
  })

  it('keeps the backend message on a refused file', async () => {
    vi.mocked(uploadAttachment).mockRejectedValue(new Error('The attachment exceeds the 25 MB limit'))
    const { result } = renderHook(() => useStagedAttachments())

    await act(async () => { result.current.addFiles([file]) })

    expect(result.current.items[0].error).toBe('The attachment exceeds the 25 MB limit')
    expect(result.current.ids).toEqual([])
  })

  it('remove deletes server-side and drops the row', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments())
    await act(async () => { result.current.addFiles([file]) })

    await act(async () => { result.current.remove(result.current.items[0].key) })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1')
    expect(result.current.items).toHaveLength(0)
  })

  it('discardAll deletes every staged id', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments())
    await act(async () => { result.current.addFiles([file]) })

    act(() => { result.current.discardAll() })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1')
  })
})
```

`AttachmentTray.test.tsx` — renders items (name, size via `formatSize`, progress bar while `progress < 1 && !error`, error text, ✕ calls `remove`), and the picker button forwards chosen files to `addFiles`.

- [ ] **Step 2: Run, verify failure** — `npm run test -- useStagedAttachments AttachmentTray`
- [ ] **Step 3: Implement**

`useStagedAttachments.ts`:

```ts
import { useCallback, useRef, useState } from 'react'
import { api, uploadAttachment } from '../../../api.js'

export interface StagedItem {
  key: string
  id: string | null
  fileName: string
  size: number
  progress: number
  error: string | null
}

let nextKey = 0

export function useStagedAttachments() {
  const [items, setItems] = useState<StagedItem[]>([])
  const itemsRef = useRef(items)
  itemsRef.current = items

  const patch = useCallback((key: string, change: Partial<StagedItem>) => {
    setItems(previous => previous.map(item => item.key === key ? { ...item, ...change } : item))
  }, [])

  const addFiles = useCallback((files: FileList | File[]) => {
    for (const file of Array.from(files)) {
      const key = `staged-${nextKey++}`
      setItems(previous => [...previous, {
        key, id: null, fileName: file.name, size: file.size, progress: 0, error: null,
      }])
      uploadAttachment(file, { onProgress: (ratio: number) => patch(key, { progress: ratio }) })
        .then((info: { id: string; size: number }) => patch(key, { id: info.id, size: info.size, progress: 1 }))
        .catch((error: Error) => patch(key, { error: error.message, progress: 1 }))
    }
  }, [patch])

  const remove = useCallback((key: string) => {
    const item = itemsRef.current.find(i => i.key === key)
    if (item?.id) api.deleteAttachment(item.id).catch(() => { /* sweeper's problem now */ })
    setItems(previous => previous.filter(i => i.key !== key))
  }, [])

  const discardAll = useCallback(() => {
    for (const item of itemsRef.current) {
      if (item.id) api.deleteAttachment(item.id).catch(() => { /* sweeper's problem now */ })
    }
    setItems([])
  }, [])

  const staged = items.filter(item => item.id !== null)
  return {
    items, addFiles, remove, discardAll,
    uploading: items.some(item => item.id === null && item.error === null),
    ids: staged.map(item => item.id as string),
  }
}
```

`AttachmentTray.tsx`:

```tsx
import { useRef } from 'react'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { formatSize } from '../reader/formatSize'
import type { StagedItem } from './useStagedAttachments'

interface Props {
  items: StagedItem[]
  onAddFiles: (files: FileList) => void
  onRemove: (key: string) => void
}

export default function AttachmentTray({ items, onAddFiles, onRemove }: Props) {
  const picker = useRef<HTMLInputElement>(null)

  return (
    <div className="compose-attachments">
      {items.map(item => (
        <span key={item.key} className={`compose-attachment${item.error ? ' is-error' : ''}`}>
          <span className="compose-attachment-name">{item.fileName}</span>
          {item.error
            ? <span className="compose-attachment-error">{item.error}</span>
            : item.progress < 1
              ? <progress value={item.progress} max={1} aria-label={`Uploading ${item.fileName}`} />
              : <span className="compose-attachment-size">{formatSize(item.size)}</span>}
          <button type="button" aria-label={`Remove ${item.fileName}`} onClick={() => onRemove(item.key)}>✕</button>
        </span>
      ))}
      <button type="button" className="btn btn-ghost compose-attach-btn" onClick={() => picker.current?.click()}>
        <PaperclipIcon size={16} /> Attach files
      </button>
      <input ref={picker} type="file" multiple hidden data-testid="attachment-input"
        onChange={e => { if (e.target.files?.length) { onAddFiles(e.target.files); e.target.value = '' } }} />
    </div>
  )
}
```

CSS:

```css
/* ── Compose: attachments ────────────────────────────────────────────────── */
.compose-attachments { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; padding: 8px 16px; border-top: 1px solid var(--border); }
.compose-attachment {
  display: inline-flex; align-items: center; gap: 6px; font-size: 12px;
  background: var(--surface-sunken); border-radius: var(--radius-sm); padding: 4px 8px;
}
.compose-attachment.is-error { border: 1px solid var(--danger); }
.compose-attachment-error { color: var(--danger); }
.compose-attachment progress { width: 80px; }
.compose-attachment button { background: none; border: none; cursor: pointer; color: inherit; padding: 0; }
```

(Check `formatSize`'s real export shape and adjust the import.)

- [ ] **Step 4: Run, verify green**, `npm run lint`.
- [ ] **Step 5: Commit** — `Frontend 2c1: staged attachment tray with progress`

---

### Task 12: ComposeView + route, entry point, leave guard

**Files:**
- Create: `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`
- Modify: `src/frontend/src/routes.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
- Modify: `src/frontend/src/modules/mail/list/SelectionToolbar.tsx` (+ its test)
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx` (forward `onCompose`)
- Modify: `src/frontend/src/modules/mail/MailLayout.test.tsx`
- Modify: `src/frontend/src/styles/mail.css`

**Interfaces:**
- Consumes: Tasks 8–11; `useAuth().identity` (From line); `useBlocker` (react-router-dom, data router — already the router in use).
- Produces: route `/mail/compose` rendered by `MailLayout` (compose mode: folders stay, `ComposeView` replaces list+splitter+reader); `SelectionToolbarProps` gains `onCompose: () => void`; `MessageList` forwards it.

- [ ] **Step 1: Failing tests**

`ComposeView.test.tsx` — mount inside a `createMemoryRouter` (routes: `/mail`, `/mail/compose`) with a `QueryClientProvider` and mocked `../../../api.js` + mocked `./SquireEditor` (a stub exposing a settable HTML and firing `onChange`; mocking here dodges jsdom/Squire again). Cover:

```
- renders From with the identity (read-only), To focused, subject, Send disabled (no recipient)
- Cc and Bcc are hidden behind the two links until clicked
- a valid To token enables Send; an invalid one disables it
- Send posts { to, cc, bcc, subject, htmlBody, attachmentIds } and navigates back to /mail?folder=… with a toast
- appendedToSent === false raises the soft warning toast wording
- a 502 send failure keeps the view and shows the error toast (staged files untouched)
- Send stays disabled while an upload is in flight
- dirty + navigate → the Discard modal appears; Keep editing stays; Discard proceeds and calls discardAll
- the ✕ with a clean form leaves without a modal
```

`SelectionToolbar.test.tsx` — add: a `New message` button renders and fires `onCompose`.

`MailLayout.test.tsx` — add (following the file's router harness): on `/mail/compose` the folder tree renders and the compose view replaces the list (assert via a `ComposeView` testid; mock heavy children as the file already does).

- [ ] **Step 2: Run, verify failure** — `npm run test -- ComposeView SelectionToolbar MailLayout`
- [ ] **Step 3: Implement**

`SelectionToolbar.tsx` — add to props `onCompose: () => void`; render **first** in `.selection-actions`, before Archive:

```tsx
<button type="button" className="selection-btn" aria-label="New message" title="New message" onClick={props.onCompose}>
  <PencilIcon size={20} />
</button>
```

(`PencilIcon` exists — `src/icons/PencilIcon.jsx`; check its props.) `MessageList` accepts `onCompose` and forwards it.

`routes.tsx` — beside the `mail` route:

```tsx
{ path: 'mail', element: <Suspense fallback={null}><MailLayout /></Suspense> },
{ path: 'mail/compose', element: <Suspense fallback={null}><MailLayout /></Suspense> },
```

`MailLayout.tsx` — compose mode:

```tsx
import { useMatch, useNavigate } from 'react-router-dom'
import ComposeView from './compose/ComposeView'
// …
const composing = useMatch('/mail/compose') != null
const navigate = useNavigate()

function selectFolder(path: string) {
  // In compose mode this is a navigation out of /mail/compose; the ComposeView blocker
  // owns the "discard?" question.
  if (composing) { navigate(`/mail?folder=${encodeURIComponent(path)}`); return }
  setParams({ folder: path })
}

const openCompose = useCallback(() => {
  navigate('/mail/compose', { state: { from: folder } })
}, [navigate, folder])
// pass onCompose={openCompose} through list()
```

and in the JSX, the three pane arrangements only render when not composing:

```tsx
{composing ? (
  <div className="mail-compose"><ComposeView onNotify={addToast} /></div>
) : (
  <>
    {pane === 'right' && ( /* unchanged */ )}
    {pane === 'bottom' && ( /* unchanged */ )}
    {pane === 'none' && ( /* unchanged */ )}
  </>
)}
```

`ComposeView.tsx`:

```tsx
import { useCallback, useEffect, useRef, useState } from 'react'
import { useBlocker, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../../contexts/AuthContext'
import { useSendMessage } from '../queries'
import AttachmentTray from './AttachmentTray'
import EditorToolbar from './EditorToolbar'
import RecipientsField, { isValidAddress } from './RecipientsField'
import SquireEditor, { type EditorHandle } from './SquireEditor'

interface Props { onNotify: (message: string, kind?: string) => void }

/**
 * The compose surface, replacing list+reader inside the mail module. No drafts yet (2c3),
 * so leaving means losing the message: a router blocker owns every exit — folder click,
 * ✕, Back — and beforeunload covers the tab.
 */
export default function ComposeView({ onNotify }: Props) {
  const { identity } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const send = useSendMessage()
  const editor = useRef<EditorHandle>(null)

  const [to, setTo] = useState<string[]>([])
  const [cc, setCc] = useState<string[]>([])
  const [bcc, setBcc] = useState<string[]>([])
  const [showCc, setShowCc] = useState(false)
  const [showBcc, setShowBcc] = useState(false)
  const [subject, setSubject] = useState('')
  const [bodyTouched, setBodyTouched] = useState(false)
  const attachments = useStagedAttachments()

  // Refs, not state, for the blocker predicate: it is evaluated at navigation time.
  const dirtyRef = useRef(false)
  const leavingRef = useRef(false)
  dirtyRef.current = to.length > 0 || cc.length > 0 || bcc.length > 0
    || subject !== '' || bodyTouched || attachments.items.length > 0

  const blocker = useBlocker(() => dirtyRef.current && !leavingRef.current)

  useEffect(() => {
    function onBeforeUnload(event: BeforeUnloadEvent) {
      if (dirtyRef.current && !leavingRef.current) event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [])

  const backTarget = (() => {
    const from = (location.state as { from?: string } | null)?.from
    return from ? `/mail?folder=${encodeURIComponent(from)}` : '/mail'
  })()

  const leave = useCallback(() => {
    leavingRef.current = true
    navigate(backTarget)
  }, [navigate, backTarget])

  const allValid = [...to, ...cc, ...bcc].every(isValidAddress)
  const canSend = to.length > 0 && allValid && !attachments.uploading && !send.isPending

  function submit() {
    send.mutate(
      { to, cc, bcc, subject, htmlBody: editor.current?.getHTML() ?? '', attachmentIds: attachments.ids },
      {
        onSuccess: (result) => {
          onNotify(result.appendedToSent ? 'Message sent' : 'Message sent — no Sent copy could be filed')
          leave()
        },
        onError: (error: Error) => onNotify(error.message || 'Could not send the message', 'error'),
      },
    )
  }

  function close() {
    if (!dirtyRef.current) { leave(); return }
    // Dirty: navigate anyway — the blocker turns it into the Discard question.
    navigate(backTarget)
  }

  return (
    <div className="compose-view" data-testid="compose-view">
      <div className="compose-header">
        <span className="modal-title">New message</span>
        <button className="modal-close" aria-label="Close" onClick={close}>✕</button>
      </div>

      <div className="compose-fields">
        <div className="field-h">
          <label htmlFor="compose-from">From</label>
          <input id="compose-from" type="text" readOnly
            value={identity ? `${identity.displayName} <${identity.email}>` : ''} />
        </div>
        <div className="compose-to-row">
          <RecipientsField id="compose-to" label="To" tokens={to} onChange={setTo} autoFocus />
          <span className="compose-cc-links">
            {!showCc && <button type="button" className="link-btn" onClick={() => setShowCc(true)}>Cc</button>}
            {!showBcc && <button type="button" className="link-btn" onClick={() => setShowBcc(true)}>Bcc</button>}
          </span>
        </div>
        {showCc && <RecipientsField id="compose-cc" label="Cc" tokens={cc} onChange={setCc} />}
        {showBcc && <RecipientsField id="compose-bcc" label="Bcc" tokens={bcc} onChange={setBcc} />}
        <div className="field-h">
          <label htmlFor="compose-subject">Subject</label>
          <input id="compose-subject" type="text" value={subject} onChange={e => setSubject(e.target.value)} />
        </div>
      </div>

      <EditorToolbar editor={editor.current} />
      <SquireEditor ref={editor} onChange={() => setBodyTouched(true)} />

      <AttachmentTray items={attachments.items} onAddFiles={attachments.addFiles} onRemove={attachments.remove} />

      <div className="compose-actions">
        <button type="button" className="btn btn-primary" disabled={!canSend} onClick={submit}>
          {send.isPending ? 'Sending…' : 'Send'}
        </button>
        <button type="button" className="btn btn-ghost" onClick={close}>Discard</button>
      </div>

      {blocker.state === 'blocked' && (
        <div className="modal-overlay">
          <div className="modal" style={{ maxWidth: '420px' }}>
            <div className="modal-header">
              <span className="modal-title">Discard this message?</span>
            </div>
            <p>Your message has not been sent and there are no drafts yet. Leaving discards it.</p>
            <div className="folder-pick-actions">
              <button type="button" className="btn btn-ghost" onClick={() => blocker.reset()}>Keep editing</button>
              <button type="button" className="btn btn-primary"
                onClick={() => { attachments.discardAll(); leavingRef.current = true; blocker.proceed() }}>
                Discard
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
```

(Import `useStagedAttachments` — omitted above for brevity in the imports list, it belongs there. One editor-handle nuance: `editor.current` is null on first render, so `EditorToolbar` receives null until a re-render — the `bodyTouched` state flip on first input provides one; if the toolbar must work before any typing, lift the handle into a `useState` set from a ref-callback instead. The implementer picks the ref-callback + state variant if the test for "toolbar works before typing" fails — cover it: add a test clicking Bold before typing.)

CSS:

```css
/* ── Compose: view ───────────────────────────────────────────────────────── */
.mail-compose { flex: 1; min-width: 0; display: flex; }
.compose-view {
  flex: 1; display: flex; flex-direction: column; overflow: hidden;
  background: var(--surface); min-width: 0;
}
.compose-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-bottom: 1px solid var(--border); }
.compose-fields { display: flex; flex-direction: column; gap: 8px; padding: 12px 16px; }
.compose-to-row { display: flex; align-items: flex-start; gap: 8px; }
.compose-to-row .recipients-field { flex: 1; }
.compose-cc-links { display: flex; gap: 8px; padding-top: 6px; }
.link-btn { background: none; border: none; color: var(--action-primary); cursor: pointer; padding: 0; font-size: 13px; }
.compose-actions { display: flex; gap: 8px; padding: 12px 16px; border-top: 1px solid var(--border); }
```

- [ ] **Step 4: Run, verify green** — `npm run test`, `npm run lint`, `npm run typecheck`.
- [ ] **Step 5: Commit** — `Frontend 2c1: compose view at /mail/compose with leave guard`

---

### Task 13: Frontend docs + full verification

**Files:**
- Modify: `src/frontend/CLAUDE.md`

- [ ] **Step 1: Docs** — update `src/frontend/CLAUDE.md`: replace "Composing is not in yet" with a Compose paragraph (route `/mail/compose` inside the mail module, folders stay; Squire wrapper with mocked-engine tests and the always-light canvas rule; staged uploads over XHR for progress; the leave guard via `useBlocker`; the `onCompose` chain MailLayout → MessageList → SelectionToolbar). Add `compose/` files to the mail module file list.
- [ ] **Step 2: Full frontend verification** — `npm run test`, `npm run test:coverage` (no regression), `npm run lint`, `npm run build`.
- [ ] **Step 3: Full backend verification** — `dotnet test` (874+ tests green), `dotnet build -c Release`.
- [ ] **Step 4: Commit** — `Docs 2c1: compose module documentation`

**Manual verification (after deploy, spec § 5):** send to Gmail/Outlook (styles survive, text fallback, attachments, Bcc invisible), Sent copy (`\Seen`, Bcc visible), oversized attachment refused with the limit named, in-flight upload blocks Send, leave guard on folder/✕/tab-close, Discard purges staged, 4 theme combinations (light canvas everywhere), SMTP failure → 502, staged kept.
