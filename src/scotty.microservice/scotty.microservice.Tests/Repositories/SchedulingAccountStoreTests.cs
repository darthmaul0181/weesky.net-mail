using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class SchedulingAccountStoreTests
{
    private const int DuplicateKeyEntry = 1062;

    private static readonly DateTime TestedAt = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _database = Guid.NewGuid().ToString("N");

    private SchedulingAccountStore CreateStore() => new(new PreferencesTestDbContext(_database));

    private Task DeleteAsync() => CreateStore().DeleteAsync(CancellationToken.None);

    private Task SaveAsync(byte[] cipher, string host = "smtp.weesky.be") =>
        CreateStore().SaveAsync(host, 587, "StartTls", "noreply-agenda@weesky.net", cipher, CancellationToken.None);

    [Fact]
    public async Task Find_OnAnEmptyTable_AnswersNull()
    {
        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Save_InsertsTheSingleRow()
    {
        await SaveAsync([1, 2, 3]);

        var row = await CreateStore().FindAsync(CancellationToken.None);
        Assert.NotNull(row);
        Assert.Equal(SchedulingServiceAccount.SingletonId, row.Id);
        Assert.Equal(("smtp.weesky.be", 587, "StartTls", "noreply-agenda@weesky.net"), (row.Host, row.Port, row.Security, row.Login));
        Assert.Equal([1, 2, 3], row.PasswordCipher);
        Assert.NotEqual(default, row.UpdatedAt);
        Assert.Null(row.LastTestAt);
        Assert.Null(row.LastTestOk);
    }

    [Fact]
    public async Task Save_WithoutACipher_KeepsTheStoredOne_AndClearsTheLastTest()
    {
        await SaveAsync([1, 2, 3]);
        var saved = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.True(await CreateStore().RecordTestAsync(TestedAt, ok: true, saved.UpdatedAt, CancellationToken.None));

        Assert.True(await CreateStore().SaveKeepingPasswordAsync(
            "mail.weesky.be", 587, "StartTls", "noreply-agenda@weesky.net", saved.UpdatedAt, CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal("mail.weesky.be", row.Host);
        Assert.Equal([1, 2, 3], row.PasswordCipher);
        Assert.Null(row.LastTestAt);
        Assert.Null(row.LastTestOk);
        Assert.Single(new PreferencesTestDbContext(_database).SchedulingServiceAccounts);
    }

    [Fact]
    public async Task Save_WithANewCipher_ReplacesTheStoredOne()
    {
        await SaveAsync([1, 2, 3]);

        await SaveAsync([9]);

        Assert.Equal([9], (await CreateStore().FindAsync(CancellationToken.None))!.PasswordCipher);
    }

    [Fact]
    public async Task SaveKeepingThePassword_OnAnEmptyTable_WritesNothing()
    {
        Assert.False(await CreateStore().SaveKeepingPasswordAsync("smtp.weesky.be", 587, "StartTls", "a@weesky.net", TestedAt, CancellationToken.None));

        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheRow_AndIsIdempotent()
    {
        await SaveAsync([1]);

        await CreateStore().DeleteAsync(CancellationToken.None);
        await CreateStore().DeleteAsync(CancellationToken.None);

        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecordTest_StampsTheRowItTested()
    {
        await SaveAsync([1]);
        var saved = (await CreateStore().FindAsync(CancellationToken.None))!;

        Assert.True(await CreateStore().RecordTestAsync(TestedAt, ok: false, saved.UpdatedAt, CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal(TestedAt, row.LastTestAt);
        Assert.False(row.LastTestOk);
        Assert.Equal(saved.UpdatedAt, row.UpdatedAt);
    }

    // A save landing while the test ran: its verdict belongs to the configuration it replaced.
    [Fact]
    public async Task RecordTest_OnARowSavedSince_WritesNothing()
    {
        await SaveAsync([1]);
        var tested = (await CreateStore().FindAsync(CancellationToken.None))!;
        await Task.Delay(5);
        await SaveAsync([2]);

        Assert.False(await CreateStore().RecordTestAsync(TestedAt, ok: true, tested.UpdatedAt, CancellationToken.None));

        Assert.Null((await CreateStore().FindAsync(CancellationToken.None))!.LastTestAt);
    }

    // The save lands between the check and the write: only the concurrency token on updated_at can see it.
    [Fact]
    public async Task RecordTest_WhenASaveCommitsDuringItsOwnWrite_WritesNothing()
    {
        await SaveAsync([1]);
        var tested = (await CreateStore().FindAsync(CancellationToken.None))!;
        await Task.Delay(5);
        var store = new SchedulingAccountStore(new InterleavedPreferencesDbContext(_database, () => SaveAsync([2], host: "saved.weesky.be")));

        Assert.False(await store.RecordTestAsync(TestedAt, ok: true, tested.UpdatedAt, CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal("saved.weesky.be", row.Host);
        Assert.Null(row.LastTestAt);
        Assert.Null(row.LastTestOk);
    }

    [Fact]
    public async Task RecordTest_OnADeletedRow_WritesNothing()
    {
        Assert.False(await CreateStore().RecordTestAsync(TestedAt, ok: true, TestedAt, CancellationToken.None));

        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    // ---- last writer wins ----

    private SchedulingAccountStore Interleaved(Func<Task> otherWriter, int races = 1) =>
        new(new InterleavedPreferencesDbContext(_database, otherWriter, races));

    [Fact]
    public async Task ASaveBringingItsPassword_ThatLosesARaceToAnotherSave_AppliesItsValuesOnceMore()
    {
        await SaveAsync([1], host: "first.weesky.be");
        var store = Interleaved(() => SaveAsync([2], host: "concurrent.weesky.be"));

        Assert.True(await store.SaveAsync("later.weesky.be", 587, "StartTls", "later@weesky.net", [3], CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal(("later.weesky.be", "later@weesky.net"), (row.Host, row.Login));
        Assert.Equal([3], row.PasswordCipher);
    }

    // Retrying would pair the concurrent save's password with this save's host: never.
    [Fact]
    public async Task ASaveKeepingThePassword_ThatLosesARaceToAnotherSave_WritesNothingAndSaysSo()
    {
        await SaveAsync([1], host: "first.weesky.be");
        var checkedRow = (await CreateStore().FindAsync(CancellationToken.None))!;
        var store = Interleaved(() => SaveAsync([2], host: "concurrent.weesky.be"));

        Assert.False(await store.SaveKeepingPasswordAsync(
            "first.weesky.be", 587, "StartTls", "later@weesky.net", checkedRow.UpdatedAt, CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal(("concurrent.weesky.be", "noreply-agenda@weesky.net"), (row.Host, row.Login));
        Assert.Equal([2], row.PasswordCipher);
    }

    // The window before any race: another save committed between the caller's check and this write.
    [Fact]
    public async Task ASaveKeepingThePassword_OfARowSavedSinceItWasChecked_WritesNothingAndSaysSo()
    {
        await SaveAsync([1], host: "first.weesky.be");
        var checkedRow = (await CreateStore().FindAsync(CancellationToken.None))!;
        await Task.Delay(5);
        await SaveAsync([2], host: "concurrent.weesky.be");

        Assert.False(await CreateStore().SaveKeepingPasswordAsync(
            "first.weesky.be", 587, "StartTls", "later@weesky.net", checkedRow.UpdatedAt, CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal("concurrent.weesky.be", row.Host);
        Assert.Equal([2], row.PasswordCipher);
    }

    [Fact]
    public async Task ASaveThatLosesTwice_WritesNothingAndSaysSo()
    {
        await SaveAsync([1], host: "first.weesky.be");
        var round = 0;
        var store = Interleaved(() => SaveAsync([2], host: $"concurrent{++round}.weesky.be"), races: 2);

        Assert.False(await store.SaveAsync("later.weesky.be", 587, "StartTls", "later@weesky.net", [3], CancellationToken.None));

        Assert.Equal("concurrent2.weesky.be", (await CreateStore().FindAsync(CancellationToken.None))!.Host);
    }

    [Fact]
    public async Task ASaveOverlappingADelete_RecreatesTheRow_WhenItCarriesAPassword()
    {
        await SaveAsync([1]);
        var store = Interleaved(DeleteAsync);

        Assert.True(await store.SaveAsync("later.weesky.be", 587, "StartTls", "later@weesky.net", [3], CancellationToken.None));

        Assert.Equal("later.weesky.be", (await CreateStore().FindAsync(CancellationToken.None))!.Host);
    }

    // The password it meant to keep went with the deleted row: there is nothing to save it with.
    [Fact]
    public async Task ASaveKeepingThePassword_OverlappingADelete_WritesNothingAndSaysSo()
    {
        await SaveAsync([1]);
        var checkedRow = (await CreateStore().FindAsync(CancellationToken.None))!;
        var store = Interleaved(DeleteAsync);

        Assert.False(await store.SaveKeepingPasswordAsync(
            "smtp.weesky.be", 587, "StartTls", "later@weesky.net", checkedRow.UpdatedAt, CancellationToken.None));

        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFirstSaveThatLosesTheInsertRace_UpdatesTheRowTheOtherInserted()
    {
        var store = new SchedulingAccountStore(new DuplicateOnFirstInsertContext(_database, () => SaveAsync([2], host: "concurrent.weesky.be")));

        Assert.True(await store.SaveAsync("later.weesky.be", 587, "StartTls", "later@weesky.net", [3], CancellationToken.None));

        var row = (await CreateStore().FindAsync(CancellationToken.None))!;
        Assert.Equal("later.weesky.be", row.Host);
        Assert.Equal([3], row.PasswordCipher);
    }

    // Last writer by commit order: the delete lands after the save, so the fresh row goes too.
    [Fact]
    public async Task ADeleteThatLosesARaceToASave_RemovesTheFreshRow()
    {
        await SaveAsync([1]);
        var store = Interleaved(() => SaveAsync([2], host: "concurrent.weesky.be"));

        Assert.True(await store.DeleteAsync(CancellationToken.None));

        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ADeleteThatLosesARaceToAnotherDelete_IsDone()
    {
        await SaveAsync([1]);
        var store = Interleaved(DeleteAsync);

        Assert.True(await store.DeleteAsync(CancellationToken.None));

        Assert.Null(await CreateStore().FindAsync(CancellationToken.None));
    }

    /// <summary>MariaDB's answer to two first INSERTs of id 1, which the InMemory provider cannot give.</summary>
    private sealed class DuplicateOnFirstInsertContext(string databaseName, Func<Task> otherWriter) : PreferencesTestDbContext(databaseName)
    {
        private bool raced;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (raced) return await base.SaveChangesAsync(cancellationToken);
            raced = true;
            await otherWriter();
            throw new DbUpdateException("Duplicate entry '1' for key 'PRIMARY'", MySqlErrors.With(DuplicateKeyEntry, "Duplicate entry"));
        }
    }
}
