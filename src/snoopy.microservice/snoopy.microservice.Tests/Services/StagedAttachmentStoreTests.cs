using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models;
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
        try { File.Delete(_root); } catch { /* not the file the I/O test leaves */ }
    }

    private static MemoryStream Bytes(int count) => new(new byte[count]);

    private Task<Result<StagedAttachmentInfo>> SaveMegabyteAsync(string name) =>
        _store.SaveAsync("me", name, "application/octet-stream", Bytes(1024 * 1024), CancellationToken.None);

    private Task<Result<StagedAttachmentInfo>> SaveTinyAsync(string accountId, string name) =>
        _store.SaveAsync(accountId, name, "application/octet-stream", Bytes(1), CancellationToken.None);

    private long StagedBytesOnDisk() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

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
    public async Task SaveAsync_CarriesTheContentIdThrough()
    {
        var result = await _store.SaveAsync("me", "logo.png", "image/png",
            new MemoryStream([1, 2, 3]), CancellationToken.None, "logo@mail");

        Assert.True(result.IsSuccess);
        Assert.Equal("logo@mail", result.Value.ContentId);
    }

    [Fact]
    public async Task SaveAsync_DefaultsContentIdToNull()
    {
        var result = await _store.SaveAsync("me", "a.pdf", "application/pdf",
            new MemoryStream([1]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ContentId);
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
        // A stable code, not prose carrying the configured limit — the client resolves it to a
        // translated sentence, so the number itself is no longer part of the wire contract.
        Assert.Equal(StagedAttachmentErrors.TooLarge, result.Error);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTraceOfAnUploadRefusedMidCopy()
    {
        for (var i = 0; i < 3; i++) Assert.True((await SaveMegabyteAsync($"f{i}.bin")).IsSuccess);

        Assert.True((await _store.SaveAsync("me", "big.bin", "application/octet-stream",
            Bytes(1024 * 1024 + 1), CancellationToken.None)).IsFailure);

        Assert.Equal(3 * 1024 * 1024, StagedBytesOnDisk());
        // The last megabyte of the ceiling is still free only if the refused upload took nothing.
        Assert.True((await SaveMegabyteAsync("ok.bin")).IsSuccess);
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

    // The scope carries the user as well as the account: one user filling their primary
    // account's ceiling must never consume another user's quota (nor reach their files).
    [Fact]
    public async Task SaveAsync_OneUsersFullCeiling_DoesNotConsumeAnotherUsersQuota()
    {
        var userA = new User("alice@weesky.be") { WebmailUid = Guid.NewGuid() };
        var userB = new User("bob@weesky.be") { WebmailUid = Guid.NewGuid() };
        var scopeA = MailAccountConnection.StagedScope(userA, MailAccountConnection.Primary);
        var scopeB = MailAccountConnection.StagedScope(userB, MailAccountConnection.Primary);

        for (var i = 0; i < 4; i++)
            Assert.True((await _store.SaveAsync(scopeA, $"a{i}.bin", "application/octet-stream",
                Bytes(1024 * 1024), CancellationToken.None)).IsSuccess);
        Assert.True((await _store.SaveAsync(scopeA, "a5.bin", "application/octet-stream",
            Bytes(1), CancellationToken.None)).IsFailure);

        var other = await _store.SaveAsync(scopeB, "b.bin", "application/octet-stream",
            Bytes(1), CancellationToken.None);

        Assert.True(other.IsSuccess);
        Assert.True(_store.Open(scopeA, other.Value.Id).IsFailure);
    }

    [Fact]
    public async Task SaveAsync_HoldsTheAccountCeilingUnderConcurrentUploads()
    {
        const int parallel = 8;
        using var arrived = new CountdownEvent(parallel);
        var uploads = Enumerable.Range(0, parallel).Select(i => Task.Run(async () =>
            await _store.SaveAsync("me", $"p{i}.bin", "application/octet-stream",
                new GatedStream(new byte[1024 * 1024], arrived), CancellationToken.None)));

        var results = await Task.WhenAll(uploads);

        Assert.True(results.Count(r => r.IsSuccess) <= 4, $"{results.Count(r => r.IsSuccess)} uploads passed the ceiling");
        Assert.True(StagedBytesOnDisk() <= 4L * 1024 * 1024, $"{StagedBytesOnDisk()} bytes staged");
    }

    [Fact]
    public async Task SaveAsync_RefusesAfterFiftyEntriesEvenWellUnderTheByteCeiling()
    {
        for (var i = 0; i < 50; i++)
            Assert.True((await SaveTinyAsync("me", $"f{i}.bin")).IsSuccess);

        var overCap = await SaveTinyAsync("me", "f50.bin");

        Assert.True(overCap.IsFailure);
        Assert.Contains("Too many staged attachments", overCap.Error);
    }

    [Fact]
    public async Task Delete_FreesAnEntryCountSlotAtTheCap()
    {
        StagedAttachmentInfo? first = null;
        for (var i = 0; i < 50; i++)
        {
            var saved = await SaveTinyAsync("me", $"f{i}.bin");
            first ??= saved.Value;
        }

        _store.Delete("me", first!.Id);

        Assert.True((await SaveTinyAsync("me", "f50.bin")).IsSuccess);
    }

    [Fact]
    public async Task SweepExpired_FreesEntryCountSlotsAtTheCap()
    {
        for (var i = 0; i < 50; i++)
            Assert.True((await SaveTinyAsync("me", $"f{i}.bin")).IsSuccess);

        _clock.Now = _clock.Now.AddHours(13);

        Assert.Equal(50, _store.SweepExpired());
        Assert.True((await SaveTinyAsync("me", "after.bin")).IsSuccess);
    }

    [Fact]
    public async Task SaveAsync_TheEntryCountCapIsPerAccount()
    {
        for (var i = 0; i < 50; i++)
            Assert.True((await SaveTinyAsync("me", $"f{i}.bin")).IsSuccess);
        Assert.True((await SaveTinyAsync("me", "over.bin")).IsFailure);

        Assert.True((await SaveTinyAsync("someone-else", "b.bin")).IsSuccess);
    }

    [Fact]
    public async Task SaveAsync_ReturnsAFailureWhenTheDiskRefusesTheWrite()
    {
        File.WriteAllText(_root, "the root is a file, so no account directory can be created");

        var result = await _store.SaveAsync("me", "a.txt", "text/plain", Bytes(4), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(_root, result.Error);
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

    [Fact]
    public async Task SweepExpired_KeepsAnEntryExactlyAtTheTtl()
    {
        var saved = await _store.SaveAsync("me", "a.txt", "text/plain", Bytes(4), CancellationToken.None);
        _clock.Now = _clock.Now.AddHours(12);

        Assert.Equal(0, _store.SweepExpired());
        Assert.True(_store.Open("me", saved.Value.Id).IsSuccess);

        _clock.Now = _clock.Now.AddTicks(1);
        Assert.Equal(1, _store.SweepExpired());
    }

    [Fact]
    public void SweepExpired_ReclaimsFilesLeftBehindByARestart()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "0123456789ABCDEF"));
        var orphan = Path.Combine(directory.FullName, $"{Guid.NewGuid():N}");
        var fresh = Path.Combine(directory.FullName, $"{Guid.NewGuid():N}");
        File.WriteAllBytes(orphan, new byte[4]);
        File.WriteAllBytes(fresh, new byte[4]);
        File.SetLastWriteTimeUtc(orphan, _clock.Now.AddHours(-13).UtcDateTime);
        File.SetLastWriteTimeUtc(fresh, _clock.Now.UtcDateTime);

        Assert.Equal(1, _store.SweepExpired());
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public async Task Open_StopsCountingAnEntryWhoseFileVanished()
    {
        var saved = await SaveMegabyteAsync("gone.bin");
        File.Delete(_store.Open("me", saved.Value.Id).Value.FilePath);

        Assert.True(_store.Open("me", saved.Value.Id).IsFailure);

        for (var i = 0; i < 4; i++) Assert.True((await SaveMegabyteAsync($"f{i}.bin")).IsSuccess);
    }

    /// <summary>Holds every upload at its first read, so all of them clear the ceiling check first.
    /// The wait is bounded: an upload refused before reading never signals.</summary>
    private sealed class GatedStream(byte[] bytes, CountdownEvent arrived) : MemoryStream(bytes)
    {
        private bool _waited;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (!_waited)
            {
                _waited = true;
                arrived.Signal();
                arrived.Wait(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
